using FluentAssertions;
using GroundUp.Auth.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Migrations;

/// <summary>
/// The <c>AddRoleNameIndexAndTenantDefaultRole</c> migration (GU-95): role names become unique per
/// tenant ignoring case (and a database that already has duplicates stops with a clear message),
/// tenants get a default role, and a held role can no longer be deleted. Each test gets its own database.
/// </summary>
public sealed class RoleIndexMigrationTests : IClassFixture<RoleIndexMigrationTests.ContainerFixture>
{
    private const string PreviousMigration = "20261127141021_AddJoinLinks";

    private readonly string _connectionString;

    public RoleIndexMigrationTests(ContainerFixture fixture)
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
        await SeedRoleAsync(tenant, "Member");
        await SeedRoleAsync(tenant, "member");

        var migrate = () => MigrateToAsync(null);

        var error = (await migrate.Should().ThrowAsync<PostgresException>()).Which;
        error.MessageText.Should().Contain("Duplicate role name \"member\"").And.Contain(tenant.ToString());
    }

    [Fact]
    public async Task Up_SameNameInTwoTenants_Applies()
    {
        await MigrateToAsync(PreviousMigration);
        await SeedRoleAsync(await SeedTenantAsync("acme"), "Member");
        await SeedRoleAsync(await SeedTenantAsync("globex"), "Member");

        await MigrateToAsync(null);

        (await ScalarAsync<long>("SELECT count(*) FROM \"AuthRoles\" WHERE \"Name\" = 'Member'")).Should().Be(2);
    }

    [Fact]
    public async Task After_ACaseOnlyDuplicateIsRefusedByTheIndex()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        await SeedRoleAsync(tenant, "Member");

        var duplicate = () => SeedRoleAsync(tenant, "MEMBER");

        await duplicate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task After_AHeldRoleCannotBeDeleted()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        var role = await SeedRoleAsync(tenant, "Member");
        var user = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthUsers\" (\"Id\", \"ExternalUserId\", \"Email\", \"DisplayName\", \"IsActive\", \"CreatedAt\") " +
            $"VALUES ('{user}', 'ext-1', 'u1@test.com', 'u1', true, now())");
        await ExecuteAsync(
            "INSERT INTO \"AuthUserRoles\" (\"Id\", \"UserId\", \"RoleId\", \"TenantId\", \"Source\", \"CreatedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{user}', '{role}', '{tenant}', 0, now())");

        var delete = () => ExecuteAsync($"DELETE FROM \"AuthRoles\" WHERE \"Id\" = '{role}'");

        await delete.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task After_TheDefaultRoleCannotBeDeleted()
    {
        await MigrateToAsync(null);
        var tenant = await SeedTenantAsync("acme");
        var role = await SeedRoleAsync(tenant, "Member");
        await ExecuteAsync($"UPDATE \"AuthTenants\" SET \"DefaultRoleId\" = '{role}' WHERE \"Id\" = '{tenant}'");

        var delete = () => ExecuteAsync($"DELETE FROM \"AuthRoles\" WHERE \"Id\" = '{role}'");

        await delete.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Down_RemovesTheIndexAndTheColumn()
    {
        await MigrateToAsync(null);

        await MigrateToAsync(PreviousMigration);

        (await ScalarAsync<long>("SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_AuthRoles_TenantId_LowerName'")).Should().Be(0);
        (await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'AuthTenants' AND column_name = 'DefaultRoleId'"))
            .Should().Be(0);
    }

    private async Task<Guid> SeedTenantAsync(string slug)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthTenants\" (\"Id\", \"Name\", \"Slug\", \"TenantType\", \"OnboardingMode\", \"IsActive\", \"CreatedAt\", \"IsDeleted\") " +
            $"VALUES ('{id}', '{slug}', '{slug}', 0, 0, true, now(), false)");
        return id;
    }

    private async Task<Guid> SeedRoleAsync(Guid tenant, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO \"AuthRoles\" (\"Id\", \"Name\", \"RoleType\", \"TenantId\", \"IsSystem\", \"CreatedAt\") " +
            $"VALUES ('{id}', '{name}', 1, '{tenant}', false, now())");
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
