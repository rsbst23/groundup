using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services.Configuration;

/// <summary>
/// Configuration options for the GroundUp auth service layer.
/// Bound from the "GroundUp:Auth" configuration section.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Cache TTL for resolved permission sets, in minutes. Default: 15.
    /// </summary>
    public int PermissionCacheTtlMinutes { get; set; } = 15;

    /// <summary>
    /// JWT claim type used to extract the user identifier. Default: "sub".
    /// </summary>
    public string UserIdClaimType { get; set; } = "sub";

    /// <summary>
    /// JWT claim type used to extract the user's email address. Default: "email".
    /// </summary>
    public string EmailClaimType { get; set; } = "email";

    /// <summary>
    /// JWT claim type used to extract the user's display name. Default: "name".
    /// </summary>
    public string DisplayNameClaimType { get; set; } = "name";

    /// <summary>
    /// JWT claim type used to extract the tenant identifier. Default: "tid".
    /// </summary>
    public string TenantIdClaimType { get; set; } = "tid";

    /// <summary>
    /// The signing key used for JWT token generation and validation.
    /// Used as the default/fallback key when no per-tenant key is configured.
    /// </summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// The issuer claim value for generated JWT tokens. Default: "GroundUp".
    /// </summary>
    public string Issuer { get; set; } = "GroundUp";

    /// <summary>
    /// The audience claim value for generated JWT tokens. Default: "GroundUp".
    /// </summary>
    public string Audience { get; set; } = "GroundUp";

    /// <summary>
    /// Token expiration time in minutes. Default: 60.
    /// </summary>
    public int TokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// The name of the authentication cookie. Default: "AuthToken".
    /// </summary>
    public string CookieName { get; set; } = "AuthToken";

    /// <summary>
    /// Whether the authentication cookie requires HTTPS. Default: true.
    /// </summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>
    /// The SameSite mode for the authentication cookie. Default: Strict.
    /// </summary>
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Strict;

    /// <summary>
    /// Absolute session lifetime cap in minutes, measured from auth_time.
    /// Sliding refresh stops after this threshold. Default: 480 (8 hours).
    /// Must be greater than 0 and >= TokenExpirationMinutes.
    /// </summary>
    public int AbsoluteSessionLifetimeMinutes { get; set; } = 480;

    /// <summary>
    /// Duration in minutes for AuthFlowState expiration. Default: 10.
    /// </summary>
    public int FlowStateExpirationMinutes { get; set; } = 10;

    /// <summary>
    /// Name of the state cookie for browser-binding CSRF protection.
    /// Default: "AuthState".
    /// </summary>
    public string StateCookieName { get; set; } = "AuthState";

    /// <summary>
    /// Callback URL path (relative) for OAuth callbacks. Default: "/auth/callback".
    /// </summary>
    public string CallbackPath { get; set; } = "/auth/callback";

    /// <summary>
    /// Interval in minutes between AuthFlowState cleanup sweeper cycles. Default: 5.
    /// Must be greater than 0; validated on startup.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Number of days to retain terminal AuthFlowState rows before hard deletion. Default: 7.
    /// Must be 0 or greater; validated on startup. 0 = delete immediately after termination.
    /// </summary>
    public int RetentionDays { get; set; } = 7;
}
