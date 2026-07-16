using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents an in-flight OAuth flow. The primary key (UUID v7) is the value
/// embedded in the OAuth state parameter sent to the identity provider.
/// Not tenant-scoped (some flows create the tenant as part of the flow).
/// Not soft-deletable (cleanup sweeper hard-deletes to keep the table bounded).
/// </summary>
public sealed class AuthFlowState : BaseEntity, IAuditable
{
    /// <summary>
    /// The type of authentication flow being executed.
    /// </summary>
    public FlowType FlowType { get; set; }

    /// <summary>
    /// Current lifecycle status. Defaults to <see cref="FlowStatus.Pending"/>.
    /// </summary>
    public FlowStatus Status { get; set; } = FlowStatus.Pending;

    /// <summary>
    /// Optional tenant identifier. May be null for flows that create the tenant.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Optional invitation identifier for invitation-based flows.
    /// </summary>
    public Guid? InvitationId { get; set; }

    /// <summary>
    /// Optional join link identifier for join-link-based flows.
    /// </summary>
    public Guid? JoinLinkId { get; set; }

    /// <summary>
    /// Optional identity provider realm name for routing.
    /// </summary>
    public string? Realm { get; set; }

    /// <summary>
    /// Optional URL to redirect the user after successful authentication.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Cryptographically-random OAuth state token — the lookup key at callback.
    /// Distinct from the PK (UUIDv7) because UUIDv7 is timestamp-based and partially predictable.
    /// </summary>
    public string StateToken { get; set; } = string.Empty;

    /// <summary>
    /// PKCE code_verifier for authorization code exchange.
    /// </summary>
    public string CodeVerifier { get; set; } = string.Empty;

    /// <summary>
    /// Exact redirect_uri used at authorize time, reused at code exchange.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Organization name for NewOrganization flows. Null for other flow types.
    /// </summary>
    public string? OrganizationName { get; set; }

    /// <summary>
    /// Cryptographic nonce for CSRF protection. Required.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the client that initiated the flow.
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// User-Agent header of the client that initiated the flow.
    /// </summary>
    public string? CreatedByUserAgent { get; set; }

    /// <summary>
    /// When this flow state expires and can no longer be consumed.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When this flow was successfully consumed. Null until consumption.
    /// </summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>
    /// When this flow reached a terminal state (consumed, failed, or expired).
    /// </summary>
    public DateTime? TerminatedAt { get; set; }

    /// <summary>
    /// Reason for failure, if the flow was marked as failed.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
