namespace GroundUp.Core.Dtos.Setup;

/// <summary>
/// A new setup wizard transaction log entry.
/// </summary>
/// <param name="Operation">The operation being performed (e.g., "first-admin-create").</param>
/// <param name="Stage">The initial stage (e.g., "keycloak-pending").</param>
/// <param name="Email">Optional email address associated with the operation.</param>
/// <param name="CorrelationId">Optional correlation ID linking related log entries.</param>
public sealed record CreateSetupTransactionLogDto(
    string Operation,
    string Stage,
    string? Email,
    string? CorrelationId);
