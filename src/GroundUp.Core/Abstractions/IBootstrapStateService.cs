using GroundUp.Core.Results;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Service for querying and completing the bootstrap state.
/// Uses IMemoryCache with key "groundup:bootstrap-state" and 60-second absolute expiration TTL.
///
/// Multi-instance cache invalidation lag is accepted: when one instance calls
/// <see cref="CompleteSetupAsync"/>, other instances may continue to see
/// <c>IsComplete=false</c> for up to 60 seconds. Setup is a one-time event
/// so this brief inconsistency window is acceptable.
/// </summary>
public interface IBootstrapStateService
{
    /// <summary>
    /// Returns whether setup is complete. Cached for 60 seconds.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if setup has been completed; otherwise <c>false</c>.</returns>
    Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks setup as complete. Fails if already complete or concurrent attempt detected
    /// (xmin optimistic concurrency). Invalidates the local cache on success.
    /// </summary>
    /// <param name="completedByUserId">The ID of the user who completed setup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or the specific conflict condition.</returns>
    Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the cached bootstrap state value immediately (local instance only).
    /// Other instances retain their cached value until their TTL expires.
    /// </summary>
    void InvalidateCache();
}
