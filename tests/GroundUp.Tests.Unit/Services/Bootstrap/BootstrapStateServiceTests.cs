using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Unit tests for <see cref="BootstrapStateService"/>.
/// Validates caching behavior, complete-setup logic, and error handling.
/// Requirements: 6.1–6.8
/// </summary>
public sealed class BootstrapStateServiceTests : IDisposable
{
    private const string CacheKey = "groundup:bootstrap-state";

    private readonly TestGroundUpDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BootstrapStateService> _logger;
    private readonly BootstrapStateService _sut;

    public BootstrapStateServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestGroundUpDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TestGroundUpDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<BootstrapStateService>>();
        _sut = new BootstrapStateService(_dbContext, _cache, _logger);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _dbContext.Dispose();
    }

    #region IsCompleteAsync Tests

    [Fact]
    public async Task IsCompleteAsync_CacheHit_ReturnsCachedValue()
    {
        // Arrange — seed cache with true, do NOT seed DB
        _cache.Set(CacheKey, true);

        // Act
        var result = await _sut.IsCompleteAsync();

        // Assert — returns cached value without DB access (no row in DB, would throw if accessed)
        Assert.True(result);
    }

    [Fact]
    public async Task IsCompleteAsync_CacheMiss_ReadsFromDb_CachesResult()
    {
        // Arrange — seed DB with incomplete state, no cache entry
        SeedBootstrapState(isComplete: false);

        // Act
        var result = await _sut.IsCompleteAsync();

        // Assert
        Assert.False(result);

        // Verify cache was populated
        Assert.True(_cache.TryGetValue(CacheKey, out bool cachedValue));
        Assert.False(cachedValue);
    }

    [Fact]
    public async Task IsCompleteAsync_CacheMiss_CompletedState_CachesTrue()
    {
        // Arrange — seed DB with completed state
        SeedBootstrapState(isComplete: true);

        // Act
        var result = await _sut.IsCompleteAsync();

        // Assert
        Assert.True(result);
        Assert.True(_cache.TryGetValue(CacheKey, out bool cachedValue));
        Assert.True(cachedValue);
    }

    [Fact]
    public async Task IsCompleteAsync_RowMissing_ThrowsInvalidOperationException()
    {
        // Arrange — no row seeded in DB, no cache entry

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.IsCompleteAsync());

        Assert.Contains("Bootstrap state row is missing", ex.Message);
    }

    #endregion

    #region CompleteSetupAsync Tests

    [Fact]
    public async Task CompleteSetupAsync_AlreadyComplete_ReturnsConflict()
    {
        // Arrange
        SeedBootstrapState(isComplete: true);
        var userId = Guid.NewGuid();

        // Act
        var result = await _sut.CompleteSetupAsync(userId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("already complete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteSetupAsync_Success_UpdatesRowAndInvalidatesCache()
    {
        // Arrange
        SeedBootstrapState(isComplete: false);
        _cache.Set(CacheKey, false);
        var userId = Guid.NewGuid();

        // Act
        var result = await _sut.CompleteSetupAsync(userId);

        // Assert
        Assert.True(result.Success);

        // Verify DB was updated
        var state = await _dbContext.BootstrapStates
            .FirstOrDefaultAsync(s => s.Id == BootstrapState.SentinelId);
        Assert.NotNull(state);
        Assert.True(state.IsComplete);
        Assert.NotNull(state.CompletedAt);
        Assert.Equal(userId, state.CompletedBy);

        // Verify cache was invalidated
        Assert.False(_cache.TryGetValue(CacheKey, out _));
    }

    [Fact]
    public async Task CompleteSetupAsync_RowMissing_Returns503()
    {
        // Arrange — no row seeded
        var userId = Guid.NewGuid();

        // Act
        var result = await _sut.CompleteSetupAsync(userId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region InvalidateCache Tests

    [Fact]
    public void InvalidateCache_RemovesCacheKey()
    {
        // Arrange
        _cache.Set(CacheKey, true);

        // Act
        _sut.InvalidateCache();

        // Assert
        Assert.False(_cache.TryGetValue(CacheKey, out _));
    }

    #endregion

    #region Helpers

    private void SeedBootstrapState(bool isComplete)
    {
        _dbContext.BootstrapStates.Add(new BootstrapState
        {
            Id = BootstrapState.SentinelId,
            IsComplete = isComplete,
            CompletedAt = isComplete ? DateTime.UtcNow : null,
            CompletedBy = isComplete ? Guid.NewGuid() : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "migration"
        });
        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Concrete DbContext for in-memory testing since GroundUpDbContext is abstract.
    /// </summary>
    private sealed class TestGroundUpDbContext : GroundUpDbContext
    {
        public TestGroundUpDbContext(DbContextOptions<TestGroundUpDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure BootstrapState for InMemory (skip Postgres-specific xmin config)
            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }

    #endregion
}
