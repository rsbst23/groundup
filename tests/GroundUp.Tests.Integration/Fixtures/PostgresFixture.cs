using GroundUp.Sample.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Fixtures;

/// <summary>
/// Shared xUnit fixture that starts a single Postgres container for the test collection.
/// Each test creates a unique database within the container for full isolation.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>
    /// The base connection string to the Postgres container.
    /// Tests should use <see cref="CreateContext"/> which creates a unique database per call.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a fresh <see cref="SampleDbContext"/> pointing to a unique database.
    /// Calls <c>EnsureCreated()</c> to apply the schema. Each test gets its own
    /// database so tests never interfere with each other.
    /// </summary>
    public SampleDbContext CreateContext()
    {
        var uniqueDb = $"test_{Guid.NewGuid():N}";
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = uniqueDb
        };

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        var context = new SampleDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="PostgresFixture"/>
/// across all test classes in the "Postgres" collection.
/// </summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
