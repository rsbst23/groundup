using FluentAssertions;
using GroundUp.Auth.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Migrations;

/// <summary>
/// The <c>AddPendingMembersAndRoleSource</c> migration (GU-68) on data written in the old shape:
/// membership <c>IsActive</c> maps to <c>Status</c>, SuperAdmin and built-in TenantAdmin rows become
/// source Provisioning, and <c>Down</c> restores the old columns. Each test gets its own database,
/// because the tests move the schema back and forth.
/// </summary>
public sealed class PendingMembersMigrationTests : IClassFixture<PendingMembersMigrationTests.ContainerFixture>
{
    private const string PreviousMigration = "20260623015515_AddPhase10cAuthDispatcherSchema";

    private readonly string _connectionString;

    public PendingMembersMigrationTests(ContainerFixture fixture)
    {
        // A database of its own per test: migrating creates it
        _connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = $"mig_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    /// <summary>One Postgres container shared by the tests in this class.</summary>
    public sealed class ContainerFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        public string ConnectionString { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();
            ConnectionString = _postgres.GetConnectionString();
        }

        public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Up_OldShapeData_MapsStatusAndSource()
    {
        var ids = await SeedOldShapeAsync();

        await MigrateToAsync(null);

        (await ScalarAsync<int>($"SELECT \"Status\" FROM \"AuthUserTenants\" WHERE \"Id\" = '{ids.ActiveMembership}'"))
            .Should().Be(0, "an active membership becomes Active");
        (await ScalarAsync<int>($"SELECT \"Status\" FROM \"AuthUserTenants\" WHERE \"Id\" = '{ids.InactiveMembership}'"))
            .Should().Be(1, "an inactive membership becomes Inactive");

        (await ScalarAsync<int>($"SELECT \"Source\" FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.SuperAdminAssignment}'"))
            .Should().Be(4, "SuperAdmin came from the bootstrap (Provisioning)");
        (await ScalarAsync<int>($"SELECT \"Source\" FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.TenantAdminAssignment}'"))
            .Should().Be(4, "the built-in TenantAdmin came from organization creation (Provisioning)");
        (await ScalarAsync<int>($"SELECT \"Source\" FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}'"))
            .Should().Be(0, "other assignments are Manual");
    }

    [Fact]
    public async Task Up_AllowsPendingUsersAndOneRoleFromSeveralSources()
    {
        var ids = await SeedOldShapeAsync();
        await MigrateToAsync(null);

        // Two pending users (no identity yet) can coexist; the unique index ignores nulls
        await ExecuteAsync(
            "INSERT INTO \"AuthUsers\" (\"Id\", \"ExternalUserId\", \"Email\", \"DisplayName\", \"IsActive\", \"CreatedAt\") VALUES " +
            $"('{Guid.NewGuid()}', NULL, 'p1@test.com', 'p1', true, now()), ('{Guid.NewGuid()}', NULL, 'p2@test.com', 'p2', true, now())");

        // The same role from a second source
        await ExecuteAsync(
            "INSERT INTO \"AuthUserRoles\" (\"Id\", \"UserId\", \"RoleId\", \"TenantId\", \"Source\", \"CreatedAt\") " +
            $"SELECT '{Guid.NewGuid()}', \"UserId\", \"RoleId\", \"TenantId\", 1, now() FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}'");

        (await ScalarAsync<long>("SELECT count(*) FROM \"AuthUsers\" WHERE \"ExternalUserId\" IS NULL")).Should().Be(2);
        (await ScalarAsync<long>($"SELECT count(*) FROM \"AuthUserRoles\" WHERE \"RoleId\" = (SELECT \"RoleId\" FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}')"))
            .Should().Be(2);

        // A second identical source row is still refused
        var duplicate = () => ExecuteAsync(
            "INSERT INTO \"AuthUserRoles\" (\"Id\", \"UserId\", \"RoleId\", \"TenantId\", \"Source\", \"CreatedAt\") " +
            $"SELECT '{Guid.NewGuid()}', \"UserId\", \"RoleId\", \"TenantId\", 1, now() FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}'");
        await duplicate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Down_RestoresIsActiveAndDropsPendingData()
    {
        var ids = await SeedOldShapeAsync();
        await MigrateToAsync(null);

        var pendingUserId = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthUsers\" (\"Id\", \"ExternalUserId\", \"Email\", \"DisplayName\", \"IsActive\", \"CreatedAt\") " +
            $"VALUES ('{pendingUserId}', NULL, 'pending@test.com', 'pending', true, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthUserTenants\" (\"Id\", \"UserId\", \"TenantId\", \"ExternalUserId\", \"Status\", \"CreatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{pendingUserId}', '{ids.Tenant}', NULL, 2, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthUserRoles\" (\"Id\", \"UserId\", \"RoleId\", \"TenantId\", \"Source\", \"CreatedAt\") " +
            $"SELECT '{Guid.NewGuid()}', \"UserId\", \"RoleId\", \"TenantId\", 1, now() FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}'");

        await MigrateToAsync(PreviousMigration);

        (await ScalarAsync<bool>($"SELECT \"IsActive\" FROM \"AuthUserTenants\" WHERE \"Id\" = '{ids.ActiveMembership}'")).Should().BeTrue();
        (await ScalarAsync<bool>($"SELECT \"IsActive\" FROM \"AuthUserTenants\" WHERE \"Id\" = '{ids.InactiveMembership}'")).Should().BeFalse();
        (await ScalarAsync<long>($"SELECT count(*) FROM \"AuthUsers\" WHERE \"Id\" = '{pendingUserId}'")).Should().Be(0);
        (await ScalarAsync<long>($"SELECT count(*) FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}' OR \"RoleId\" = (SELECT \"RoleId\" FROM \"AuthUserRoles\" WHERE \"Id\" = '{ids.OtherAssignment}')"))
            .Should().Be(1, "duplicate-source rows are reduced to one for the old unique index");
    }

    private sealed record SeededIds(
        Guid Tenant,
        Guid ActiveMembership,
        Guid InactiveMembership,
        Guid SuperAdminAssignment,
        Guid TenantAdminAssignment,
        Guid OtherAssignment);

    /// <summary>Migrates to the previous migration and inserts rows in the old shape.</summary>
    private async Task<SeededIds> SeedOldShapeAsync()
    {
        await MigrateToAsync(PreviousMigration);

        var systemTenant = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var superAdminRole = Guid.NewGuid();
        var tenantAdminRole = Guid.NewGuid();
        var editorRole = Guid.NewGuid();
        var ids = new SeededIds(tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await ExecuteAsync(
            "INSERT INTO \"AuthTenants\" (\"Id\", \"Name\", \"Slug\", \"TenantType\", \"OnboardingMode\", \"IsActive\", \"CreatedAt\", \"IsDeleted\") VALUES " +
            $"('{systemTenant}', 'System', 'system', 0, 0, true, now(), false), ('{tenant}', 'Acme', 'acme', 0, 0, true, now(), false)");
        await ExecuteAsync(
            "INSERT INTO \"AuthUsers\" (\"Id\", \"ExternalUserId\", \"Email\", \"DisplayName\", \"IsActive\", \"CreatedAt\") VALUES " +
            $"('{user1}', 'ext-1', 'u1@test.com', 'u1', true, now()), ('{user2}', 'ext-2', 'u2@test.com', 'u2', true, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthRoles\" (\"Id\", \"Name\", \"RoleType\", \"TenantId\", \"IsSystem\", \"CreatedAt\") VALUES " +
            $"('{superAdminRole}', 'SuperAdmin', 0, '{systemTenant}', true, now()), " +
            $"('{tenantAdminRole}', 'TenantAdmin', 1, '{tenant}', true, now()), " +
            $"('{editorRole}', 'Editor', 1, '{tenant}', false, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthUserTenants\" (\"Id\", \"UserId\", \"TenantId\", \"ExternalUserId\", \"IsActive\", \"CreatedAt\") VALUES " +
            $"('{ids.ActiveMembership}', '{user1}', '{tenant}', 'ext-1', true, now()), " +
            $"('{ids.InactiveMembership}', '{user2}', '{tenant}', 'ext-2', false, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthUserRoles\" (\"Id\", \"UserId\", \"RoleId\", \"TenantId\", \"CreatedAt\") VALUES " +
            $"('{ids.SuperAdminAssignment}', '{user1}', '{superAdminRole}', '{systemTenant}', now()), " +
            $"('{ids.TenantAdminAssignment}', '{user1}', '{tenantAdminRole}', '{tenant}', now()), " +
            $"('{ids.OtherAssignment}', '{user1}', '{editorRole}', '{tenant}', now())");

        return ids;
    }

    private AuthDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(_connectionString).Options);

    /// <summary>Migrates to <paramref name="target"/>, or to the latest migration when null.</summary>
    private async Task MigrateToAsync(string? target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
