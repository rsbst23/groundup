using FluentAssertions;
using GroundUp.Core.Entities;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests verifying that concurrent calls to
/// <see cref="BootstrapStateService.CompleteSetupAsync"/> against a real Postgres
/// database result in exactly one winner via the xmin optimistic concurrency token.
/// </summary>
public sealed class BootstrapConcurrencyTests : IAsyncLifetime
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

        // Create schema and seed the singleton BootstrapState row
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Seed the singleton row (EnsureCreated doesn't run migration seed data)
        context.BootstrapStates.Add(new BootstrapState
        {
            Id = BootstrapState.SentinelId,
            IsComplete = false,
            CompletedAt = null,
            CompletedBy = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-seed"
        });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task CompleteSetupAsync_MultipleConcurrentCalls_ExactlyOneWinsViaXmin()
    {
        // Arrange
        const int concurrency = 10;
        var userIds = Enumerable.Range(0, concurrency)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        // Act — launch N concurrent CompleteSetupAsync calls, each with its own DbContext
        var tasks = userIds.Select(userId => Task.Run(async () =>
        {
            await using var context = CreateContext();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = NullLogger<BootstrapStateService>.Instance;
            var service = new BootstrapStateService(context, cache, logger);
            return await service.CompleteSetupAsync(userId);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert — exactly one succeeds, all others return Conflict (409)
        var successes = results.Where(r => r.Success).ToList();
        var failures = results.Where(r => !r.Success).ToList();

        successes.Should().HaveCount(1, "exactly one concurrent call should win the xmin race");
        failures.Should().HaveCount(concurrency - 1, "all other calls should fail with conflict");

        foreach (var failure in failures)
        {
            failure.StatusCode.Should().Be(409);
        }
    }

    [Fact]
    public async Task CompleteSetupAsync_MultipleConcurrentCalls_WinnerSetsCompletedByAndCompletedAt()
    {
        // Arrange — reset the row to incomplete for this test
        await ResetBootstrapStateAsync();

        const int concurrency = 5;
        var userIds = Enumerable.Range(0, concurrency)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        // Act
        var tasks = userIds.Select(userId => Task.Run(async () =>
        {
            await using var context = CreateContext();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = NullLogger<BootstrapStateService>.Instance;
            var service = new BootstrapStateService(context, cache, logger);
            var result = await service.CompleteSetupAsync(userId);
            return (UserId: userId, Result: result);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert — the winner's userId is persisted
        var winner = results.Single(r => r.Result.Success);

        await using var verifyContext = CreateContext();
        var state = await verifyContext.BootstrapStates
            .AsNoTracking()
            .FirstAsync(s => s.Id == BootstrapState.SentinelId);

        state.IsComplete.Should().BeTrue();
        state.CompletedBy.Should().Be(winner.UserId);
        state.CompletedAt.Should().NotBeNull();
        state.CompletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CompleteSetupAsync_AfterAlreadyComplete_ReturnsConflict()
    {
        // Arrange — complete setup first
        await ResetBootstrapStateAsync();

        await using (var context = CreateContext())
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = NullLogger<BootstrapStateService>.Instance;
            var service = new BootstrapStateService(context, cache, logger);
            var firstResult = await service.CompleteSetupAsync(Guid.NewGuid());
            firstResult.Success.Should().BeTrue();
        }

        // Act — try to complete again
        await using var secondContext = CreateContext();
        var secondCache = new MemoryCache(new MemoryCacheOptions());
        var secondLogger = NullLogger<BootstrapStateService>.Instance;
        var secondService = new BootstrapStateService(secondContext, secondCache, secondLogger);
        var result = await secondService.CompleteSetupAsync(Guid.NewGuid());

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    private TestBootstrapDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestBootstrapDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new TestBootstrapDbContext(options);
    }

    private async Task ResetBootstrapStateAsync()
    {
        await using var context = CreateContext();
        var state = await context.BootstrapStates
            .FirstAsync(s => s.Id == BootstrapState.SentinelId);
        state.IsComplete = false;
        state.CompletedAt = null;
        state.CompletedBy = null;
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Concrete DbContext for integration tests. Inherits from GroundUpDbContext
    /// so all entity configurations (including xmin concurrency token) are applied.
    /// </summary>
    private sealed class TestBootstrapDbContext : GroundUpDbContext
    {
        public TestBootstrapDbContext(DbContextOptions<TestBootstrapDbContext> options)
            : base(options) { }
    }
}
