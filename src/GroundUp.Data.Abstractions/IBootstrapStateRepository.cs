namespace GroundUp.Data.Abstractions;

/// <summary>
/// Data access for the singleton bootstrap state row that records whether first-run setup is complete.
/// </summary>
public interface IBootstrapStateRepository
{
    /// <summary>
    /// Reads whether setup is complete.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completion flag, or <c>null</c> when the singleton row is missing.</returns>
    Task<bool?> GetIsCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks setup complete (completion time in UTC and the completing user), unless the row is missing,
    /// already complete, or changed concurrently by another caller.
    /// </summary>
    /// <param name="completedByUserId">The user completing setup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened; only <see cref="BootstrapCompletionOutcome.Completed"/> saves anything.</returns>
    Task<BootstrapCompletionOutcome> TryCompleteAsync(Guid completedByUserId, CancellationToken cancellationToken = default);
}
