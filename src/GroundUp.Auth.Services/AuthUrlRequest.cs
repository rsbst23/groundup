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
/// <param name="MaxAgeSeconds">
/// When set, appends <c>max_age</c>: the identity provider re-authenticates the user if they last
/// signed in longer ago than this, and must include <c>auth_time</c> in the ID token.
/// </param>
/// <param name="ForceLogin">
/// When true (and not a sign-up), appends <c>prompt=login</c> so the identity provider asks for
/// credentials even if its single sign-on session is still alive.
/// </param>
/// <param name="LoginHint">When set, appended as <c>login_hint</c> to pre-fill the email field.</param>
public sealed record AuthUrlRequest(
    string? RealmOverride,
    string RedirectUri,
    bool IsSignup = false,
    int? MaxAgeSeconds = null,
    bool ForceLogin = false,
    string? LoginHint = null);
