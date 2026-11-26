using GroundUp.Auth.Core.Enums;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Data.Postgres.Seeders;
using GroundUp.Auth.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// First-admin provisioning through the real <see cref="IdentityBootstrapService"/> against Postgres
/// (GU-81). The setup wizard's integration tests replace this service with a fake, so this is its
/// only end-to-end coverage. Written before the service moves off <c>AuthDbContext</c>; the
/// assertions must keep passing after the move (only <see cref="CreateService"/> may change).
/// </summary>
[Collection("IdentityBootstrapPostgres")]
public sealed class IdentityBootstrapServiceIntegrationTests
{
    private readonly IdentityBootstrapPostgresFixture _fixture;

    public IdentityBootstrapServiceIntegrationTests(IdentityBootstrapPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Builds the service under test. The only part of this class allowed to change in GU-81.</summary>
    private IIdentityBootstrapService CreateService(AuthDbContext context)
        => new IdentityBootstrapService(
            new GroundUp.Auth.Repositories.IdentityBootstrapRepository(context, new GroundUp.Data.Postgres.PostgresUniqueConstraintDetector()),
            NullLogger<IdentityBootstrapService>.Instance);

    [Fact]
    public async Task FirstAdminProvisioning_FullLifecycle()
    {
        // The database starts empty; the system role seeder creates the System tenant and SuperAdmin role
        await using (var context = _fixture.CreateContext())
        {
            await new DefaultSystemRoleSeeder(context).SeedAsync();
        }

        // ── Before provisioning ──
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context);
            (await service.HasSystemTenantAsync()).Should().BeTrue();
            (await service.HasSuperAdminRoleAsync()).Should().BeTrue();
            (await service.HasSuperAdminAsync()).Should().BeFalse();
            (await service.GetSuperAdminUserIdAsync()).Should().BeNull();
        }

        var request = new ProvisionFirstSuperAdminRequest(
            "first-admin@example.com", "First Admin", "kc-first-admin", AuthRoleNames.SystemTenantId);

        // ── First call creates user, membership and SuperAdmin role ──
        Guid userId;
        await using (var context = _fixture.CreateContext())
        {
            var result = await CreateService(context).ProvisionFirstSuperAdminAsync(request);

            result.Success.Should().BeTrue(result.Message);
            result.Data!.AlreadyExisted.Should().BeFalse();
            result.Data.Email.Should().Be(request.Email);
            userId = result.Data.UserId;
        }

        await using (var context = _fixture.CreateContext())
        {
            var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == request.Email);
            user.Id.Should().Be(userId);
            user.DisplayName.Should().Be(request.DisplayName);
            user.ExternalUserId.Should().Be(request.ExternalUserId);
            user.IsActive.Should().BeTrue();

            var membership = await context.UserTenants.AsNoTracking()
                .SingleAsync(ut => ut.UserId == userId && ut.TenantId == AuthRoleNames.SystemTenantId);
            membership.Status.Should().Be(MembershipStatus.Active);
            membership.ExternalUserId.Should().Be(request.ExternalUserId);

            var role = await context.UserRoles.AsNoTracking().Include(ur => ur.Role)
                .SingleAsync(ur => ur.UserId == userId);
            role.Role.Name.Should().Be(AuthRoleNames.SuperAdmin);
            role.TenantId.Should().Be(AuthRoleNames.SystemTenantId);
        }

        // ── After provisioning ──
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateService(context);
            (await service.HasSuperAdminAsync()).Should().BeTrue();
            (await service.GetSuperAdminUserIdAsync()).Should().Be(userId);
        }

        // ── Same input again: idempotent, nothing duplicated ──
        await using (var context = _fixture.CreateContext())
        {
            var repeat = await CreateService(context).ProvisionFirstSuperAdminAsync(request);

            repeat.Success.Should().BeTrue(repeat.Message);
            repeat.Data!.AlreadyExisted.Should().BeTrue();
            repeat.Data.UserId.Should().Be(userId);
        }

        await using (var context = _fixture.CreateContext())
        {
            (await context.Users.CountAsync(u => u.Email == request.Email)).Should().Be(1);
            (await context.UserTenants.CountAsync(ut => ut.UserId == userId)).Should().Be(1);
            (await context.UserRoles.CountAsync(ur => ur.UserId == userId)).Should().Be(1);
        }

        // ── Same email, different display name: conflict ──
        await using (var context = _fixture.CreateContext())
        {
            var conflict = await CreateService(context).ProvisionFirstSuperAdminAsync(
                request with { DisplayName = "Someone Else" });

            conflict.Success.Should().BeFalse();
            conflict.StatusCode.Should().Be(409);
        }
    }
}

/// <summary>
/// Own Postgres container, so SuperAdmin rows seeded by other auth tests cannot change the answers.
/// </summary>
public sealed class IdentityBootstrapPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public AuthDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_connectionString).Options);
}

[CollectionDefinition("IdentityBootstrapPostgres")]
public sealed class IdentityBootstrapPostgresCollection : ICollectionFixture<IdentityBootstrapPostgresFixture>
{
}
