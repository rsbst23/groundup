namespace GroundUp.Data.Abstractions;

/// <summary>
/// Result of <see cref="IBootstrapStateRepository.TryCompleteAsync"/>.
/// </summary>
public enum BootstrapCompletionOutcome
{
    /// <summary>The row was marked complete by this call.</summary>
    Completed,

    /// <summary>The singleton bootstrap state row does not exist.</summary>
    Missing,

    /// <summary>Setup was already complete before this call.</summary>
    AlreadyComplete,

    /// <summary>Another caller changed the row at the same time (optimistic concurrency); nothing was saved.</summary>
    ConcurrentUpdate
}
