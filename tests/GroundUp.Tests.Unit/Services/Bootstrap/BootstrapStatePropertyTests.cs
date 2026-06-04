using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core;
using GroundUp.Core.Entities;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Property-based tests for <see cref="BootstrapStateService"/>.
/// Validates one-shot completion correctness properties from the Phase 10AB design document.
///
/// Property 7: Bootstrap One-Shot Completion Under Concurrency — Exactly one of N
/// concurrent calls succeeds; rest return Conflict.
///
/// The concurrency token (xmin) enforcement is tested at the integration level with
/// a real Postgres database (task 38.3). At the unit level, we verify the logical
/// one-shot invariant: the service correctly transitions from incomplete to complete
/// exactly once, and all subsequent attempts return Conflict.
/// </summary>
public sealed class BootstrapStatePropertyTests : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<BootstrapStateService> _logger;

    public BootstrapStatePropertyTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<BootstrapStateService>>();
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    /// <summary>
    /// Property 7: Bootstrap One-Shot Completion Under Concurrency.
    /// For any sequence of N callers (N >= 2), exactly one CompleteSetupAsync call
    /// succeeds and the rest return Conflict (status 409). The bootstrap state is a
    /// one-shot transition from incomplete to complete.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CompleteSetupAsync_MultipleCalls_ExactlyOneSucceeds(PositiveInt callerCount)
    {
        // Constrain N to [2, 50] for meaningful concurrency testing
        var n = Math.Clamp(callerCount.Get, 2, 50);

        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();
            using var dbContext = CreateDbContext(dbName);
            SeedBootstrapState(dbContext, isComplete: false);

            // Simulate N callers each getting their own service instance
            // (sharing the same DB but with separate cache instances)
            var results = new OperationResult[n];
            for (var i = 0; i < n; i++)
            {
                // Each caller gets a fresh context to simulate scoped DI
                using var ctx = CreateDbContext(dbName);
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var logger = Substitute.For<ILogger<BootstrapStateService>>();
                var sut = new BootstrapStateService(ctx, cache, logger);
                results[i] = sut.CompleteSetupAsync(Guid.NewGuid()).GetAwaiter().GetResult();
            }

            var successCount = results.Count(r => r.Success);
            var conflictCount = results.Count(r => !r.Success && r.StatusCode == 409);

            // Exactly one succeeds, the rest are conflicts
            return successCount == 1 && conflictCount == n - 1;
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 7 (corollary): After one-shot completion, all subsequent calls return Conflict.
    /// For any user ID, calling CompleteSetupAsync after the state is already complete
    /// always returns Conflict regardless of the caller identity.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CompleteSetupAsync_AfterCompletion_AlwaysReturnsConflict(Guid userId, PositiveInt additionalAttempts)
    {
        var attempts = Math.Clamp(additionalAttempts.Get, 1, 20);

        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            // First: complete setup successfully
            using (var ctx = CreateDbContext(dbName))
            {
                SeedBootstrapState(ctx, isComplete: false);
            }

            using (var ctx = CreateDbContext(dbName))
            {
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var logger = Substitute.For<ILogger<BootstrapStateService>>();
                var sut = new BootstrapStateService(ctx, cache, logger);
                var firstResult = sut.CompleteSetupAsync(Guid.NewGuid()).GetAwaiter().GetResult();
                if (!firstResult.Success) return false;
            }

            // Then: all subsequent calls should return Conflict
            for (var i = 0; i < attempts; i++)
            {
                using var ctx = CreateDbContext(dbName);
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var logger = Substitute.For<ILogger<BootstrapStateService>>();
                var sut = new BootstrapStateService(ctx, cache, logger);
                var result = sut.CompleteSetupAsync(userId).GetAwaiter().GetResult();

                if (result.Success || result.StatusCode != 409)
                    return false;
            }

            return true;
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property 7 (invariant): CompletedBy and CompletedAt are set on the winning call.
    /// For any user ID that wins the one-shot completion, the persisted row has
    /// CompletedBy == that user ID and CompletedAt is non-null and recent.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CompleteSetupAsync_WinningCall_SetsCompletedByAndCompletedAt(Guid winnerId)
    {
        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            using (var ctx = CreateDbContext(dbName))
            {
                SeedBootstrapState(ctx, isComplete: false);
            }

            using (var ctx = CreateDbContext(dbName))
            {
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var logger = Substitute.For<ILogger<BootstrapStateService>>();
                var sut = new BootstrapStateService(ctx, cache, logger);
                var result = sut.CompleteSetupAsync(winnerId).GetAwaiter().GetResult();

                if (!result.Success) return false;
            }

            // Verify the persisted state
            using (var ctx = CreateDbContext(dbName))
            {
                var state = ctx.BootstrapStates
                    .AsNoTracking()
                    .FirstOrDefault(s => s.Id == BootstrapState.SentinelId);

                return state is not null
                    && state.IsComplete
                    && state.CompletedBy == winnerId
                    && state.CompletedAt is not null
                    && state.CompletedAt.Value <= DateTime.UtcNow
                    && state.CompletedAt.Value > DateTime.UtcNow.AddMinutes(-1);
            }
        };

        // Filter out empty Guid since it's the sentinel ID used by BootstrapState itself
        return property.When(winnerId != Guid.Empty);
    }

    /// <summary>
    /// Property 7 (concurrency simulation): When DbUpdateConcurrencyException is thrown
    /// during SaveChanges (simulating xmin token mismatch), the service returns Conflict.
    /// For any user ID, a concurrency exception always maps to a 409 Conflict result.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CompleteSetupAsync_ConcurrencyException_ReturnsConflict(Guid userId)
    {
        Func<bool> property = () =>
        {
            var dbName = Guid.NewGuid().ToString();

            // Seed the state as incomplete
            using (var seedCtx = CreateDbContext(dbName))
            {
                SeedBootstrapState(seedCtx, isComplete: false);
            }

            // Create a context that will throw DbUpdateConcurrencyException on SaveChanges
            using var ctx = CreateConcurrencyThrowingDbContext(dbName);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var logger = Substitute.For<ILogger<BootstrapStateService>>();
            var sut = new BootstrapStateService(ctx, cache, logger);

            var result = sut.CompleteSetupAsync(userId).GetAwaiter().GetResult();

            return !result.Success
                && result.StatusCode == 409
                && result.ErrorCode == ErrorCodes.Conflict;
        };

        return property.When(userId != Guid.Empty);
    }

    #region Helpers

    private static TestBootstrapDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestBootstrapDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new TestBootstrapDbContext(options);
    }

    private static ConcurrencyThrowingDbContext CreateConcurrencyThrowingDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ConcurrencyThrowingDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ConcurrencyThrowingDbContext(options);
    }

    private static void SeedBootstrapState(GroundUpDbContext ctx, bool isComplete)
    {
        ctx.BootstrapStates.Add(new BootstrapState
        {
            Id = BootstrapState.SentinelId,
            IsComplete = isComplete,
            CompletedAt = isComplete ? DateTime.UtcNow : null,
            CompletedBy = isComplete ? Guid.NewGuid() : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "migration"
        });
        ctx.SaveChanges();
    }

    /// <summary>
    /// Concrete DbContext for in-memory testing since GroundUpDbContext is abstract.
    /// Skips the Postgres-specific xmin configuration.
    /// </summary>
    private sealed class TestBootstrapDbContext : GroundUpDbContext
    {
        public TestBootstrapDbContext(DbContextOptions<TestBootstrapDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Skip base.OnModelCreating to avoid Postgres-specific configurations
            // (xmin column type "xid", CHECK constraints, etc.)
            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    /// <summary>
    /// DbContext that throws DbUpdateConcurrencyException on SaveChangesAsync
    /// to simulate xmin concurrency token mismatch in a real Postgres environment.
    /// </summary>
    private sealed class ConcurrencyThrowingDbContext : GroundUpDbContext
    {
        public ConcurrencyThrowingDbContext(DbContextOptions<ConcurrencyThrowingDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Simulate xmin concurrency token mismatch
            throw new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), but actually affected 0 row(s).",
                new List<Microsoft.EntityFrameworkCore.Update.IUpdateEntry>());
        }
    }

    #endregion
}
