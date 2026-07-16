using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for AuthFlowState entities. Provides standard CRUD operations
/// plus atomic one-shot consumption, failure marking, and bulk sweeper operations.
/// </summary>
public interface IAuthFlowStateRepository : IBaseRepository<AuthFlowStateDto>
{
    /// <summary>
    /// Atomically marks a Pending, non-expired row as Consumed.
    /// Returns conflict if already consumed/failed, expired failure if past ExpiresAt.
    /// </summary>
    /// <param name="id">The unique identifier of the flow state to consume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The consumed DTO on success, or a failure result.</returns>
    Task<OperationResult<AuthFlowStateDto>> MarkConsumedAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a Pending row as Failed with a reason.
    /// Returns conflict if already terminal.
    /// </summary>
    /// <param name="id">The unique identifier of the flow state to fail.</param>
    /// <param name="reason">The failure reason (truncated to 1024 chars if exceeded).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The failed DTO on success, or a failure result.</returns>
    Task<OperationResult<AuthFlowStateDto>> MarkFailedAsync(
        Guid id, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-updates all Pending rows with ExpiresAt &lt;= cutoff to Expired.
    /// Returns the count of rows updated.
    /// </summary>
    /// <param name="cutoff">The expiration cutoff timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count of rows transitioned to Expired.</returns>
    Task<OperationResult<int>> MarkExpiredOlderThanAsync(
        DateTime cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-deletes all terminal (Consumed/Expired/Failed) rows with TerminatedAt &lt;= cutoff.
    /// Returns the count of rows deleted.
    /// </summary>
    /// <param name="cutoff">The retention cutoff timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count of rows deleted.</returns>
    Task<OperationResult<int>> DeleteTerminalOlderThanAsync(
        DateTime cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an AuthFlowState by its cryptographic state token (the OAuth <c>state</c> parameter value).
    /// Returns the DTO if found, or a NotFound result if no matching row exists.
    /// </summary>
    /// <param name="stateToken">The state token to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching AuthFlowState DTO on success, or a NotFound result.</returns>
    Task<OperationResult<AuthFlowStateDto>> FindByStateTokenAsync(
        string stateToken, CancellationToken cancellationToken = default);
}
