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
    /// JWT claim type used to extract the tenant identifier. Default: "tenant_id".
    /// </summary>
    public string TenantIdClaimType { get; set; } = "tenant_id";

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
}
