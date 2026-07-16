namespace GroundUp.Auth.Services.Configuration;

/// <summary>
/// Configuration options for the Keycloak identity provider.
/// Populated from the settings database via <c>ISettingsService</c> at startup
/// and refreshed automatically when settings change at runtime.
/// </summary>
public sealed class KeycloakOptions
{
    /// <summary>
    /// The public-facing base URL for Keycloak (used for browser-accessible links such as admin console URLs).
    /// Resolved from setting key <c>auth.keycloak.public-base-url</c>.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The shared realm name used for standard (non-enterprise) tenants.
    /// Resolved from setting key <c>auth.keycloak.shared-realm-name</c>.
    /// </summary>
    public string SharedRealmName { get; set; } = string.Empty;

    /// <summary>
    /// The internal base URL for Keycloak (used for server-to-server HTTP calls).
    /// Resolved from setting key <c>auth.keycloak.internal-base-url</c>.
    /// </summary>
    public string InternalBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The client ID for the Keycloak admin service account (client_credentials grant).
    /// Resolved from setting key <c>auth.keycloak.admin-client-id</c>.
    /// </summary>
    public string AdminClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret for the Keycloak admin service account.
    /// Stored encrypted at rest; resolved transparently via <c>ISettingsService</c>.
    /// Resolved from setting key <c>auth.keycloak.admin-client-secret</c>.
    /// </summary>
    public string AdminClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The OAuth2 client ID used for the consuming application (authorization code flow).
    /// Resolved from setting key <c>auth.keycloak.app-client-id</c>.
    /// </summary>
    public string AppClientId { get; set; } = string.Empty;
}
