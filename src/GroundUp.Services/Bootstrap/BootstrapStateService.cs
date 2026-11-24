using GroundUp.Core.Entities;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
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

    private readonly IBootstrapStateRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BootstrapStateService> _logger;

    public BootstrapStateService(
        IBootstrapStateRepository repository,
        IMemoryCache cache,
        ILogger<BootstrapStateService> logger)
    {
        _repository = repository;
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

        var isComplete = await _repository.GetIsCompleteAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "Bootstrap state row is missing. Restore from a database backup or re-run the migration.");

        _cache.Set(CacheKey, isComplete, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl
        });

        return isComplete;
    }

    /// <inheritdoc />
    public async Task<OperationResult> CompleteSetupAsync(
        Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        var outcome = await _repository.TryCompleteAsync(completedByUserId, cancellationToken);

        switch (outcome)
        {
            case BootstrapCompletionOutcome.Missing:
                return OperationResult.Fail("Bootstrap state row is missing.", 503);

            case BootstrapCompletionOutcome.AlreadyComplete:
                return OperationResult.Fail("Setup is already complete.", 409, Core.ErrorCodes.Conflict);

            case BootstrapCompletionOutcome.ConcurrentUpdate:
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
