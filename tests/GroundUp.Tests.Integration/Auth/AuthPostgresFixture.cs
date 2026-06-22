using GroundUp.Auth.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Shared xUnit collection fixture that provides a single Postgres container
/// for all Auth integration tests. This prevents Docker resource exhaustion
/// from spawning too many containers when running the full test suite.
/// </summary>
public sealed class AuthPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        // Apply migrations once
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AuthDbContext(options);
    }
}

/// <summary>
/// xUnit collection definition for auth integration tests sharing a Postgres container.
/// </summary>
[CollectionDefinition("AuthPostgres")]
public sealed class AuthPostgresCollection : ICollectionFixture<AuthPostgresFixture>
{
}
