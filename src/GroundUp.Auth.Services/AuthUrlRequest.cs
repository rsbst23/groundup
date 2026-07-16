namespace GroundUp.Auth.Services;

/// <summary>
/// Request parameters for building a Keycloak authorization URL.
/// </summary>
/// <param name="RealmOverride">
/// Optional realm override for enterprise tenants with a dedicated Keycloak realm.
/// When null, the shared realm from KeycloakOptions is used.
/// </param>
/// <param name="RedirectUri">The absolute redirect URI for the OAuth2 callback (e.g., https://app.example.com/auth/callback).</param>
public sealed record AuthUrlRequest(
    string? RealmOverride,
    string RedirectUri);
