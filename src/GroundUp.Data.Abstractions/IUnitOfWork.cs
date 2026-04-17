using GroundUp.Core.Results;

namespace GroundUp.Data.Abstractions;

/// <summary>
/// Provides transactional execution of multiple repository operations.
/// The consuming code passes a delegate containing the operations to execute
/// within a single database transaction. Commits on success, rolls back on failure.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Executes the provided delegate within a database transaction.
    /// Commits on success, rolls back on failure.
    /// </summary>
    /// <param name="operation">The async delegate containing operations to execute in a transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success result on commit, or a failure result on rollback.</returns>
    Task<OperationResult> ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
