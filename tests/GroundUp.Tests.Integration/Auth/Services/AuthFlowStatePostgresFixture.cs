using GroundUp.Auth.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Shared xUnit fixture that provides a Postgres container for AuthFlowState integration tests.
/// Uses a collection fixture so it's compatible with FsCheck.Xunit [Property] tests
/// (which don't properly support per-class IAsyncLifetime).
/// </summary>
public sealed class AuthFlowStatePostgresFixture : IAsyncLifetime
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

    /// <summary>
    /// Creates a fresh AuthDbContext using the container's connection string.
    /// Each test should use its own context instance for isolation.
    /// </summary>
    public AuthDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AuthDbContext(options);
    }

    /// <summary>
    /// Cleans the AuthFlowStates table between tests to prevent data contamination.
    /// </summary>
    public void CleanFlowStates()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw("DELETE FROM \"AuthFlowStates\"");
    }
}

/// <summary>
/// xUnit collection definition for AuthFlowState integration tests.
/// </summary>
[CollectionDefinition("AuthFlowStatePostgres")]
public sealed class AuthFlowStatePostgresCollection : ICollectionFixture<AuthFlowStatePostgresFixture>
{
}
