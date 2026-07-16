using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Result returned by an <see cref="IFlowHandler"/> after processing an OAuth callback.
/// Represents one of three outcomes: success (token issued), tenant selection required
/// (pending-selection state), or error (flow failed).
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
    /// When true, the user has multiple eligible memberships and must select a tenant.
    /// No GroundUp token is issued — the Keycloak token is retained as the auth cookie.
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
    /// Creates a successful result with an issued token and optional redirect URL.
    /// </summary>
    /// <param name="token">The issued GroundUp JWT token.</param>
    /// <param name="redirectUrl">Optional post-authentication redirect URL.</param>
    /// <returns>A success <see cref="FlowResult"/>.</returns>
    public static FlowResult Success(string token, string? redirectUrl = null) =>
        new() { IsSuccess = true, Token = token, RedirectUrl = redirectUrl };

    /// <summary>
    /// Creates a pending-selection result. NO GroundUp token is issued — the Keycloak token
    /// (already validated at callback) is retained as the auth cookie by the handler.
    /// The principal stays identity-only (no tid) until POST /auth/set-tenant.
    /// </summary>
    /// <param name="tenants">The list of eligible tenants for selection.</param>
    /// <returns>A tenant-selection-required <see cref="FlowResult"/>.</returns>
    public static FlowResult TenantSelectionRequired(List<TenantListItemDto> tenants) =>
        new() { IsSuccess = true, RequiresTenantSelection = true, TenantList = tenants };

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
