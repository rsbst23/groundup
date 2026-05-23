using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of an <see cref="Entities.AuthFlowState"/> for service layer responses.
/// </summary>
/// <param name="Id">The unique identifier (also the OAuth state parameter value).</param>
/// <param name="FlowType">The type of authentication flow.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="TenantId">Optional tenant identifier.</param>
/// <param name="InvitationId">Optional invitation identifier.</param>
/// <param name="JoinLinkId">Optional join link identifier.</param>
/// <param name="Realm">Optional identity provider realm name.</param>
/// <param name="ReturnUrl">Optional post-authentication redirect URL.</param>
/// <param name="Nonce">Cryptographic nonce for CSRF protection.</param>
/// <param name="CreatedByIp">IP address of the initiating client.</param>
/// <param name="CreatedByUserAgent">User-Agent of the initiating client.</param>
/// <param name="ExpiresAt">When this flow state expires.</param>
/// <param name="ConsumedAt">When this flow was consumed, if applicable.</param>
/// <param name="TerminatedAt">When this flow reached a terminal state.</param>
/// <param name="FailureReason">Reason for failure, if applicable.</param>
/// <param name="CreatedAt">When this flow was created.</param>
/// <param name="UpdatedAt">When this flow was last updated.</param>
public record AuthFlowStateDto(
    Guid Id,
    FlowType FlowType,
    FlowStatus Status,
    Guid? TenantId,
    Guid? InvitationId,
    Guid? JoinLinkId,
    string? Realm,
    string? ReturnUrl,
    string Nonce,
    string? CreatedByIp,
    string? CreatedByUserAgent,
    DateTime ExpiresAt,
    DateTime? ConsumedAt,
    DateTime? TerminatedAt,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
