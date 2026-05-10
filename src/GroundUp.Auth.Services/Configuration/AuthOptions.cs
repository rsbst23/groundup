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
}
