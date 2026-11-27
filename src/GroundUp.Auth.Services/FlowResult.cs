using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Result returned by an <see cref="IFlowHandler"/> after processing an OAuth callback.
/// Represents one of three outcomes: success (tenant-scoped token issued), tenantless (signed in,
/// no current tenant: tenant selection required or nothing to enter), or error (flow failed).
/// </summary>
public sealed record FlowResult
{
    /// <summary>
    /// Whether the flow completed successfully. True for both token-issued success
    /// and tenant-selection-required (which is a successful intermediate state).
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The issued GroundUp JWT token. Present only on full success (not during pending selection).
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// Optional post-authentication redirect URL for the client.
    /// </summary>
    public string? RedirectUrl { get; init; }

    /// <summary>
    /// When true, the user is signed in but has no current tenant. A tenantless GroundUp token
    /// was written to the auth cookie by the handler (identity only: no <c>tid</c>, no roles).
    /// <see cref="Token"/> stays null because the response must not hand out a tenant-scoped token.
    /// </summary>
    public bool IsTenantless { get; init; }

    /// <summary>
    /// When true, the user has multiple eligible memberships and must select a tenant.
    /// The session is tenantless until <c>POST /auth/set-tenant</c> selects one.
    /// </summary>
    public bool RequiresTenantSelection { get; init; }

    /// <summary>
    /// The list of eligible tenants for selection. Present only when
    /// <see cref="RequiresTenantSelection"/> is true.
    /// </summary>
    public List<TenantListItemDto>? TenantList { get; init; }

    /// <summary>
    /// Machine-readable error code. Present only when <see cref="IsSuccess"/> is false.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message. Present only when <see cref="IsSuccess"/> is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Suggested HTTP status code for the error response. Present only when <see cref="IsSuccess"/> is false.
    /// </summary>
    public int? HttpStatus { get; init; }

    /// <summary>
    /// True when an invitation or join link was used by someone who was already a member: they are
    /// signed in to the tenant and their roles did not change (GU-68).
    /// </summary>
    public bool AlreadyMember { get; init; }

    /// <summary>
    /// Creates a successful result with an issued token and optional redirect URL.
    /// </summary>
    /// <param name="token">The issued GroundUp JWT token.</param>
    /// <param name="redirectUrl">Optional post-authentication redirect URL.</param>
    /// <param name="alreadyMember">See <see cref="AlreadyMember"/>.</param>
    /// <returns>A success <see cref="FlowResult"/>.</returns>
    public static FlowResult Success(string token, string? redirectUrl = null, bool alreadyMember = false) =>
        new() { IsSuccess = true, Token = token, RedirectUrl = redirectUrl, AlreadyMember = alreadyMember };

    /// <summary>
    /// Creates a pending-selection result. The handler has written a tenantless GroundUp token
    /// to the auth cookie: the session is identity-only (no tid) until POST /auth/set-tenant.
    /// </summary>
    /// <param name="tenants">The list of eligible tenants for selection.</param>
    /// <returns>A tenant-selection-required <see cref="FlowResult"/>.</returns>
    public static FlowResult TenantSelectionRequired(List<TenantListItemDto> tenants) =>
        new() { IsSuccess = true, IsTenantless = true, RequiresTenantSelection = true, TenantList = tenants };

    /// <summary>
    /// Creates a result for a signed-in user who has no tenant to enter (zero memberships and no
    /// default tenant configured). The handler has written a tenantless GroundUp token to the auth
    /// cookie. The user can create an organization or be invited into one later.
    /// </summary>
    /// <returns>A tenantless <see cref="FlowResult"/> with an empty tenant list.</returns>
    public static FlowResult NoCurrentTenant() =>
        new() { IsSuccess = true, IsTenantless = true, TenantList = new List<TenantListItemDto>() };

    /// <summary>
    /// Creates an error result indicating the flow failed.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code (e.g., "NONCE_MISMATCH", "ACCESS_DENIED").</param>
    /// <param name="message">Human-readable error message.</param>
    /// <param name="httpStatus">Suggested HTTP status code. Defaults to 400.</param>
    /// <returns>An error <see cref="FlowResult"/>.</returns>
    public static FlowResult Error(string errorCode, string message, int httpStatus = 400) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = message, HttpStatus = httpStatus };
}
