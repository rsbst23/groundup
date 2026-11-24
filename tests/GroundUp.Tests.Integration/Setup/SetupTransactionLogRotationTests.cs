using FluentAssertions;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Repositories.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Verifies setup transaction log rotation against real Postgres: rows beyond the limit are removed
/// oldest first inside the insert's transaction, and pending rows are never removed.
/// </summary>
public sealed class SetupTransactionLogRotationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task InsertWithRotationAsync_OverLimit_KeepsPendingRowsAndRemovesOldestFinishedRows()
    {
        // Arrange: limit 4; six rows, the two oldest pending
        var pendingKeycloak = await SeedAsync("keycloak-pending", -60);
        var pendingDb = await SeedAsync("db-pending", -50);
        var oldestFinished = await SeedAsync("completed", -40);
        var secondFinished = await SeedAsync("failed", -30);
        var thirdFinished = await SeedAsync("completed", -20);
        var newestFinished = await SeedAsync("completed", -10);

        // Act
        Guid insertedId;
        await using (var context = CreateContext(audited: true))
        {
            var sut = new SetupTransactionLogRepository(context, NullLogger<SetupTransactionLogRepository>.Instance);
            insertedId = await sut.InsertWithRotationAsync(
                new CreateSetupTransactionLogDto("first-admin-create", "keycloak-pending", "a@example.com", "corr"), 4);
        }

        // Assert: 6 + 1 - 4 = 3 removed, all from the finished rows, oldest first
        await using var verify = CreateContext();
        var remaining = await verify.SetupTransactionLogs.AsNoTracking().Select(l => l.Id).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { pendingKeycloak, pendingDb, newestFinished, insertedId });
        remaining.Should().NotContain(new[] { oldestFinished, secondFinished, thirdFinished });
    }

    [Fact]
    public async Task InsertWithRotationAsync_AllRowsPending_InsertsWithoutRemovingAny()
    {
        // Arrange
        await using (var clean = CreateContext())
        {
            await clean.SetupTransactionLogs.ExecuteDeleteAsync();
        }

        var first = await SeedAsync("keycloak-pending", -20);
        var second = await SeedAsync("db-pending", -10);

        // Act
        Guid insertedId;
        await using (var context = CreateContext(audited: true))
        {
            var sut = new SetupTransactionLogRepository(context, NullLogger<SetupTransactionLogRepository>.Instance);
            insertedId = await sut.InsertWithRotationAsync(
                new CreateSetupTransactionLogDto("first-admin-create", "completed", null, null), 1);
        }

        // Assert
        await using var verify = CreateContext();
        var remaining = await verify.SetupTransactionLogs.AsNoTracking().Select(l => l.Id).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { first, second, insertedId });
    }

    private async Task<Guid> SeedAsync(string stage, int minutesAgo)
    {
        await using var context = CreateContext();
        var row = new SetupTransactionLog
        {
            Id = Guid.NewGuid(),
            Operation = "first-admin-create",
            Stage = stage,
            CreatedAt = DateTime.UtcNow.AddMinutes(minutesAgo),
            CreatedBy = "test-seed"
        };
        context.SetupTransactionLogs.Add(row);
        await context.SaveChangesAsync();
        return row.Id;
    }

    /// <summary>
    /// Creates a context. <paramref name="audited"/> adds the production audit interceptor (which sets
    /// CreatedAt on insert); seeding uses an unaudited context so the seeded timestamps are kept.
    /// </summary>
    private TestSetupDbContext CreateContext(bool audited = false)
    {
        var builder = new DbContextOptionsBuilder<TestSetupDbContext>()
            .UseNpgsql(_connectionString);
        if (audited)
        {
            builder.AddInterceptors(new AuditableInterceptor(new ServiceCollection().BuildServiceProvider()));
        }

        return new TestSetupDbContext(builder.Options);
    }

    private sealed class TestSetupDbContext : GroundUpDbContext
    {
        public TestSetupDbContext(DbContextOptions<TestSetupDbContext> options)
            : base(options) { }
    }
}
