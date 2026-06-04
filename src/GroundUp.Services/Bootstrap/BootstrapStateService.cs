using GroundUp.Core.Entities;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GroundUp.Services.Bootstrap;

/// <summary>
/// Scoped service that manages the singleton <see cref="BootstrapState"/> row.
/// Uses <see cref="IMemoryCache"/> with 60-second absolute expiration.
///
/// Multi-instance cache invalidation lag is accepted: when one instance calls
/// <see cref="CompleteSetupAsync"/>, other instances may continue to see
/// <c>IsComplete=false</c> for up to 60 seconds. Setup is a one-time event
/// so this brief inconsistency window is acceptable.
/// </summary>
public sealed class BootstrapStateService : IBootstrapStateService
{
    private const string CacheKey = "groundup:bootstrap-state";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly GroundUpDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BootstrapStateService> _logger;

    public BootstrapStateService(
        GroundUpDbContext dbContext,
        IMemoryCache cache,
        ILogger<BootstrapStateService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out bool cachedValue))
        {
            return cachedValue;
        }

        var state = await _dbContext.BootstrapStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == BootstrapState.SentinelId, cancellationToken);

        if (state is null)
        {
            throw new InvalidOperationException(
                "Bootstrap state row is missing. Restore from a database backup or re-run the migration.");
        }

        _cache.Set(CacheKey, state.IsComplete, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl
        });

        return state.IsComplete;
    }

    /// <inheritdoc />
    public async Task<OperationResult> CompleteSetupAsync(
        Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        var state = await _dbContext.BootstrapStates
            .FirstOrDefaultAsync(s => s.Id == BootstrapState.SentinelId, cancellationToken);

        if (state is null)
        {
            return OperationResult.Fail("Bootstrap state row is missing.", 503);
        }

        if (state.IsComplete)
        {
            return OperationResult.Fail("Setup is already complete.", 409, Core.ErrorCodes.Conflict);
        }

        state.IsComplete = true;
        state.CompletedAt = DateTime.UtcNow;
        state.CompletedBy = completedByUserId;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrent setup completion detected for user {UserId}.", completedByUserId);
            return OperationResult.Fail("Concurrent setup completion detected.", 409, Core.ErrorCodes.Conflict);
        }

        InvalidateCache();

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }
}
