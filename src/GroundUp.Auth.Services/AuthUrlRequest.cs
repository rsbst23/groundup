namespace GroundUp.Auth.Services;

/// <summary>
/// Request parameters for building a Keycloak authorization URL.
/// </summary>
/// <param name="RealmOverride">
/// Optional realm override for enterprise tenants with a dedicated Keycloak realm.
/// When null, the shared realm from KeycloakOptions is used.
/// </param>
/// <param name="RedirectUri">The absolute redirect URI for the OAuth2 callback (e.g., https://app.example.com/auth/callback).</param>
/// <param name="IsSignup">
/// When true, appends <c>prompt=create</c> to the authorization URL so that
/// users land on Keycloak's registration form directly. Requires Keycloak 26.1 or later.
/// </param>
public sealed record AuthUrlRequest(
    string? RealmOverride,
    string RedirectUri,
    bool IsSignup = false);
