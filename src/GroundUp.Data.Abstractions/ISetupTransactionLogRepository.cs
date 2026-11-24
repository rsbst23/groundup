using GroundUp.Core.Dtos.Setup;

namespace GroundUp.Data.Abstractions;

/// <summary>
/// Data access for the setup wizard's transaction log, which records in-progress steps so that
/// partial failures can be diagnosed and recovered.
/// </summary>
public interface ISetupTransactionLogRepository
{
    /// <summary>
    /// Inserts a row. When <paramref name="maxRowCount"/> is above zero, the oldest rows beyond the limit
    /// are removed in the same transaction, except rows in a pending stage (<c>keycloak-pending</c>,
    /// <c>db-pending</c>), which are never removed. A warning is logged when pending rows prevent
    /// reaching the limit; the insert still happens.
    /// </summary>
    /// <param name="row">The new row.</param>
    /// <param name="maxRowCount">The row limit; zero or less disables rotation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id of the inserted row.</returns>
    Task<Guid> InsertWithRotationAsync(
        CreateSetupTransactionLogDto row, int maxRowCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the stage of a row. <paramref name="externalUserId"/> and <paramref name="errorMessage"/> are
    /// written only when not null; otherwise their stored values are kept. Does nothing when the row does not exist.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="stage">The new stage.</param>
    /// <param name="externalUserId">The identity provider user id to record, or null to keep the current value.</param>
    /// <param name="errorMessage">The error to record, or null to keep the current value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateStageAsync(
        Guid id, string stage, string? externalUserId = null, string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the id of the most recent unfinished first-admin row (stage <c>keycloak-pending</c>,
    /// <c>db-pending</c> or <c>failed</c>), or null when there is none.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Guid?> GetLatestUnfinishedFirstAdminIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent rows, newest first.
    /// </summary>
    /// <param name="count">The maximum number of rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SetupTransactionLogDto>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the row with this id, or null.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SetupTransactionLogDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
