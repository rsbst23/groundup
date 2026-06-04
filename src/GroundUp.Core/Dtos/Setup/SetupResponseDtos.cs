namespace GroundUp.Core.Dtos.Setup;

/// <summary>
/// Result of a completed setup wizard step.
/// </summary>
/// <param name="Step">The name of the step that was executed.</param>
/// <param name="Completed">Whether the step completed successfully.</param>
public sealed record StepResultDto(string Step, bool Completed);

/// <summary>
/// Result of the Keycloak bootstrap step, including the provisioned client ID.
/// </summary>
/// <param name="Step">The name of the step that was executed.</param>
/// <param name="Completed">Whether the step completed successfully.</param>
/// <param name="ClientId">The Keycloak client ID that was provisioned for service-account access.</param>
public sealed record KeycloakBootstrapResultDto(string Step, bool Completed, string ClientId);

/// <summary>
/// Result of the first admin creation step.
/// </summary>
/// <param name="Step">The name of the step that was executed.</param>
/// <param name="Completed">Whether the step completed successfully.</param>
/// <param name="UserId">The database user ID of the provisioned admin.</param>
/// <param name="Email">The email address of the provisioned admin.</param>
public sealed record FirstAdminResultDto(string Step, bool Completed, Guid UserId, string Email);

/// <summary>
/// Result of a recovery operation for a partially-failed setup step.
/// </summary>
/// <param name="Recovered">Whether the recovery was successful.</param>
/// <param name="UserId">The user ID that was recovered or completed.</param>
public sealed record RecoverResultDto(bool Recovered, Guid UserId);

/// <summary>
/// A transaction log entry from the setup wizard, used for diagnosing partial failures.
/// </summary>
/// <param name="Id">The unique identifier of the log entry.</param>
/// <param name="Operation">The operation being performed (e.g., "first-admin").</param>
/// <param name="Stage">The current stage of the operation (e.g., "keycloak-pending", "completed").</param>
/// <param name="CorrelationId">Optional correlation ID linking related log entries.</param>
/// <param name="ExternalUserId">Optional external user ID from the identity provider.</param>
/// <param name="Email">Optional email address associated with the operation.</param>
/// <param name="ErrorMessage">Optional error message if the operation failed.</param>
/// <param name="CreatedAt">When the log entry was created.</param>
public sealed record SetupTransactionLogDto(
    Guid Id,
    string Operation,
    string Stage,
    string? CorrelationId,
    string? ExternalUserId,
    string? Email,
    string? ErrorMessage,
    DateTime CreatedAt);

/// <summary>
/// Current status of the setup wizard, indicating which steps have been completed.
/// </summary>
/// <param name="IsComplete">Whether the entire setup process is complete.</param>
/// <param name="CurrentStep">The name of the current (next required) step.</param>
/// <param name="AppIdentityCompleted">Whether the app identity step has been completed.</param>
/// <param name="IdentityProviderCompleted">Whether the identity provider step has been completed.</param>
/// <param name="KeycloakBootstrapCompleted">Whether the Keycloak bootstrap step has been completed.</param>
/// <param name="FirstAdminCompleted">Whether the first admin creation step has been completed.</param>
/// <param name="FirstAdminPending">Whether a first admin creation is in progress (partially completed).</param>
/// <param name="FirstAdminPendingTransactionLogId">The transaction log ID of the pending first admin operation, if any.</param>
public sealed record SetupStatusDto(
    bool IsComplete,
    string CurrentStep,
    bool AppIdentityCompleted,
    bool IdentityProviderCompleted,
    bool KeycloakBootstrapCompleted,
    bool FirstAdminCompleted,
    bool FirstAdminPending,
    string? FirstAdminPendingTransactionLogId);
