using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Auth.Repositories;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Revocation;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// GU-68 Requirements 3 and 4 against real Postgres: the role service with the real repositories.
/// The caller is a TenantAdmin of the tenant.
/// </summary>
[Collection("AuthPostgres")]
public sealed class UserRoleServiceIntegrationTests : AuthIntegrationTestBase
{
    public UserRoleServiceIntegrationTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AssignRoleAsync_ConcurrentSameGrant_OneRowAndBothSucceed()
    {
        var (tenantId, adminId, roleId, targetId) = await SeedTenantWithAdminAndMemberAsync();

        // Two requests on their own connections, as two web requests would be
        await using var context1 = Fixture.CreateContext();
        await using var context2 = Fixture.CreateContext();
        var service1 = CreateService(context1, adminId, tenantId);
        var service2 = CreateService(context2, adminId, tenantId);

        var results = await Task.WhenAll(
            service1.AssignRoleAsync(targetId, roleId),
            service2.AssignRoleAsync(targetId, roleId));

        results.Should().OnlyContain(r => r.Success);
        await using var verify = Fixture.CreateContext();
        (await verify.UserRoles.CountAsync(ur => ur.UserId == targetId && ur.RoleId == roleId)).Should().Be(1);
    }

    [Fact]
    public async Task RemoveBySourceAsync_LeavesAManualGrantOfTheSameRole()
    {
        var (tenantId, adminId, roleId, targetId) = await SeedTenantWithAdminAndMemberAsync();
        await using var context = Fixture.CreateContext();
        var service = CreateService(context, adminId, tenantId);
        IRoleGrantWriter writer = service;

        (await writer.GrantAsync(targetId, roleId, tenantId, RoleAssignmentSource.Invitation)).Success.Should().BeTrue();
        (await service.AssignRoleAsync(targetId, roleId)).Success.Should().BeTrue();

        // An invitation revoked: only its own grant goes
        (await writer.RemoveBySourceAsync(targetId, tenantId, RoleAssignmentSource.Invitation)).Success.Should().BeTrue();

        await using var verify = Fixture.CreateContext();
        var rows = await verify.UserRoles.AsNoTracking().Where(ur => ur.UserId == targetId && ur.RoleId == roleId).ToListAsync();
        rows.Should().ContainSingle().Which.Source.Should().Be(RoleAssignmentSource.Manual);
    }

    [Fact]
    public async Task RemoveRoleAsync_RemovesEverySourceAndRevokesTheTenantsTokens()
    {
        var (tenantId, adminId, roleId, targetId) = await SeedTenantWithAdminAndMemberAsync();
        var store = new InMemoryTokenRevocationStore(TimeProvider.System);
        await using var context = Fixture.CreateContext();
        var service = CreateService(context, adminId, tenantId, store);
        IRoleGrantWriter writer = service;
        var issuedBefore = DateTimeOffset.UtcNow.AddSeconds(-1);

        await writer.GrantAsync(targetId, roleId, tenantId, RoleAssignmentSource.Invitation);
        await service.AssignRoleAsync(targetId, roleId);

        (await service.RemoveRoleAsync(targetId, roleId)).Success.Should().BeTrue();

        await using var verify = Fixture.CreateContext();
        (await verify.UserRoles.CountAsync(ur => ur.UserId == targetId && ur.RoleId == roleId)).Should().Be(0);
        (await store.IsRevokedAsync(targetId, tenantId, issuedBefore)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveRoleAsync_LastTenantAdmin_Refused()
    {
        var (tenantId, adminId, _, _) = await SeedTenantWithAdminAndMemberAsync();
        await using var context = Fixture.CreateContext();
        var service = CreateService(context, adminId, tenantId);
        var adminRoleId = await context.Roles
            .Where(r => r.TenantId == tenantId && r.Name == AuthRoleNames.TenantAdmin)
            .Select(r => r.Id).SingleAsync();

        var result = await service.RemoveRoleAsync(adminId, adminRoleId);

        result.ErrorCode.Should().Be(AuthErrorCodes.LastAdmin);
    }

    [Fact]
    public async Task AssignRoleAsync_PendingMember_RoleStoredButNotEffective()
    {
        var (tenantId, adminId, roleId, _) = await SeedTenantWithAdminAndMemberAsync();
        var pendingId = await SeedPendingUserAsync($"p-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(pendingId, tenantId, MembershipStatus.Pending);
        await using var context = Fixture.CreateContext();

        (await CreateService(context, adminId, tenantId).AssignRoleAsync(pendingId, roleId)).StatusCode.Should().Be(201);

        var effective = await new UserRoleRepository(context, CreateTenantContext(tenantId)).GetByUserIdForTenantAsync(pendingId, tenantId);
        effective.Data.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>A tenant with its TenantAdmin (the caller), an Editor role and an active member.</summary>
    private async Task<(Guid TenantId, Guid AdminId, Guid RoleId, Guid TargetId)> SeedTenantWithAdminAndMemberAsync()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var roles = CreateRoleRepository(tenantId);
        var adminRole = await roles.AddAsync(new RoleDto(Guid.Empty, AuthRoleNames.TenantAdmin, null, RoleType.Application, tenantId, true));
        var editor = await roles.AddAsync(new RoleDto(Guid.Empty, "Editor", null, RoleType.Application, tenantId, false));

        var adminId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"admin-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(adminId, tenantId);
        await CreateUserRoleRepository(tenantId).AddAsync(
            new UserRoleDto(Guid.Empty, adminId, adminRole.Data!.Id, tenantId, Source: RoleAssignmentSource.Provisioning));

        var targetId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"member-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(targetId, tenantId);

        return (tenantId, adminId, editor.Data!.Id, targetId);
    }

    private UserRoleService CreateService(
        AuthDbContext context, Guid callerId, Guid tenantId, InMemoryTokenRevocationStore? store = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(callerId);
        var tenantContext = CreateTenantContext(tenantId);
        var userRoles = new UserRoleRepository(context, tenantContext);

        return new UserRoleService(
            currentUser,
            tenantContext,
            userRoles,
            new UserTenantRepository(context, tenantContext),
            new RoleRepository(context, tenantContext),
            new UserRepository(context),
            new RoleGrantPolicy(userRoles, new RoleRepository(context, tenantContext), Substitute.For<IPermissionService>(), tenantContext, []),
            new LastAdminGuard(userRoles),
            new TenantAccessRevoker(
                store ?? new InMemoryTokenRevocationStore(TimeProvider.System),
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<TenantAccessRevoker>.Instance),
            NullLogger<UserRoleService>.Instance);
    }
}
