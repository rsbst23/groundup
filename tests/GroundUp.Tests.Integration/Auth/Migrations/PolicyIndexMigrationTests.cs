using FluentAssertions;
using GroundUp.Auth.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Migrations;

/// <summary>
/// The <c>AddPolicyNameIndexAndRestrictPolicyDelete</c> migration (GU-96): policy names become unique
/// per tenant ignoring case (a database that already has duplicates stops with a clear message), and a
/// policy used by a role can no longer be deleted. Each test gets its own database.
/// </summary>
public sealed class PolicyIndexMigrationTests : IClassFixture<PolicyIndexMigrationTests.ContainerFixture>
{
    private const string PreviousMigration = "20261127230735_AddRoleNameIndexAndTenantDefaultRole";

    private readonly string _connectionString;

    public PolicyIndexMigrationTests(ContainerFixture fixture)
    {
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
    public async Task Up_DuplicateNameIgnoringCaseInOneTenant_StopsWithTheNameAndTenant()
    {
        await MigrateToAsync(PreviousMigration);
        var tenant = await SeedTenantAsync("acme");
        await SeedPolicyAsync(tenant, "Editors");
        await SeedPolicyAsync(tenant, "editors");

        var migrate = () => MigrateToAsync(null);

        var error = (await migrate.Should().ThrowAsync<PostgresException>()).Which;
        error.MessageText.Should().Contain("Duplicate policy name \"editors\"").And.Contain(tenant.ToString());
    }

    [Fact]
    public async Task Up_SameNameInTwoTenants_Applies()
    {
        await MigrateToAsync(PreviousMigration);
        await SeedPolicyAsync(await SeedTenantAsync("acme"), "Editors");
        await SeedPolicyAsync(await SeedTenantAsync("globex"), "Editors");

        await MigrateToAsync(null);

        (await ScalarAsync<long>("SELECT count(*) FROM \"AuthPolicies\" WHERE \"Name\" = 'Editors'")).Should().Be(2);
    }

    [Fact]
    public async Task After_ACaseOnlyDuplicateIsRefusedByTheIndex()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        await SeedPolicyAsync(tenant, "Editors");

        var duplicate = () => SeedPolicyAsync(tenant, "EDITORS");

        await duplicate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task After_APolicyUsedByARoleCannotBeDeleted_ButAnUnusedOneTakesItsPermissionLinks()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        var used = await SeedPolicyAsync(tenant, "Used");
        var unused = await SeedPolicyAsync(tenant, "Unused");
        var role = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthRoles\" (\"Id\", \"Name\", \"RoleType\", \"TenantId\", \"IsSystem\", \"CreatedAt\") " +
            $"VALUES ('{role}', 'Editor', 1, '{tenant}', false, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthRolePolicies\" (\"Id\", \"RoleId\", \"PolicyId\", \"CreatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{role}', '{used}', now())");
        var permission = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthPermissions\" (\"Id\", \"Key\", \"Name\", \"Module\") " +
            $"VALUES ('{permission}', 'test.things.read', 'Read things', 'test')");
        await ExecuteAsync(
            "INSERT INTO \"AuthPolicyPermissions\" (\"Id\", \"PolicyId\", \"PermissionId\", \"CreatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{unused}', '{permission}', now())");

        var deleteUsed = () => ExecuteAsync($"DELETE FROM \"AuthPolicies\" WHERE \"Id\" = '{used}'");
        await deleteUsed.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);

        await ExecuteAsync($"DELETE FROM \"AuthPolicies\" WHERE \"Id\" = '{unused}'");
        (await ScalarAsync<long>($"SELECT count(*) FROM \"AuthPolicyPermissions\" WHERE \"PolicyId\" = '{unused}'")).Should().Be(0);
    }

    [Fact]
    public async Task After_DeletingARoleStillRemovesItsPolicyLinks()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        var policy = await SeedPolicyAsync(tenant, "Editors");
        var role = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthRoles\" (\"Id\", \"Name\", \"RoleType\", \"TenantId\", \"IsSystem\", \"CreatedAt\") " +
            $"VALUES ('{role}', 'Editor', 1, '{tenant}', false, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthRolePolicies\" (\"Id\", \"RoleId\", \"PolicyId\", \"CreatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{role}', '{policy}', now())");

        await ExecuteAsync($"DELETE FROM \"AuthRoles\" WHERE \"Id\" = '{role}'");

        (await ScalarAsync<long>($"SELECT count(*) FROM \"AuthRolePolicies\" WHERE \"PolicyId\" = '{policy}'")).Should().Be(0);
    }

    [Fact]
    public async Task Down_RemovesTheIndexAndRestoresTheCascade()
    {
        await MigrateToAsync(null);

        await MigrateToAsync(PreviousMigration);

        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_AuthPolicies_TenantId_LowerName'")).Should().Be(0);
        (await ScalarAsync<string>(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'FK_AuthRolePolicies_AuthPolicies_PolicyId'"))
            .Should().Be("c", "c is ON DELETE CASCADE");
    }

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthTenants\" (\"Id\", \"Name\", \"Slug\", \"TenantType\", \"OnboardingMode\", \"IsActive\", \"CreatedAt\", \"IsDeleted\") " +
            $"VALUES ('{id}', '{slug}', '{slug}', 0, 0, true, now(), false)");
        return id;
    }

    private async Task<Guid> SeedPolicyAsync(Guid tenant, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthPolicies\" (\"Id\", \"Name\", \"TenantId\", \"CreatedAt\") " +
            $"VALUES ('{id}', '{name}', '{tenant}', now())");
        return id;
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
