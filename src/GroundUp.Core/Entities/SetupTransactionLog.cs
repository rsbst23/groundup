namespace GroundUp.Core.Entities;

/// <summary>
/// Persistent record of in-progress wizard steps so that partial failures
/// (e.g., Keycloak user created but DB write failed) can be diagnosed and
/// recovered via the /setup/recover endpoint without manual cleanup.
/// </summary>
public sealed class SetupTransactionLog : BaseEntity, IAuditable
{
    /// <summary>
    /// The wizard operation being performed (e.g., "first-admin", "keycloak-bootstrap").
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// The current stage of the operation (e.g., "keycloak-pending", "db-pending", "completed").
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Optional correlation ID linking related log entries across retries.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// The external user ID from the identity provider (e.g., Keycloak subject ID).
    /// </summary>
    public string? ExternalUserId { get; set; }

    /// <summary>
    /// The email address associated with this operation, if applicable.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Error message captured when the operation fails at a particular stage.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
