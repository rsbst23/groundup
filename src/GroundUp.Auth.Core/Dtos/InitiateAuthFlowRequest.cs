using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to initiate a new authentication flow. Validated by
/// <see cref="Validators.InitiateAuthFlowRequestValidator"/>.
/// </summary>
/// <param name="FlowType">The type of authentication flow to initiate.</param>
/// <param name="TenantId">Optional tenant identifier for tenant-scoped flows.</param>
/// <param name="InvitationId">Optional invitation identifier for invitation flows.</param>
/// <param name="JoinLinkId">Optional join link identifier for join-link flows.</param>
/// <param name="Realm">Optional identity provider realm name.</param>
/// <param name="ReturnUrl">Optional post-authentication redirect URL.</param>
/// <param name="Nonce">Cryptographic nonce for CSRF protection. Required.</param>
/// <param name="CreatedByIp">IP address of the initiating client.</param>
/// <param name="CreatedByUserAgent">User-Agent of the initiating client.</param>
/// <param name="Lifetime">Optional custom lifetime. Defaults to 15 minutes if null.</param>
public record InitiateAuthFlowRequest(
    FlowType FlowType,
    Guid? TenantId,
    Guid? InvitationId,
    Guid? JoinLinkId,
    string? Realm,
    string? ReturnUrl,
    string Nonce,
    string? CreatedByIp,
    string? CreatedByUserAgent,
    TimeSpan? Lifetime);
