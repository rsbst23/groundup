namespace GroundUp.Auth.Services;

/// <summary>
/// Result of building a Keycloak authorization URL, containing the URL and all
/// cryptographic parameters that must be persisted on the AuthFlowState for
/// callback validation and code exchange.
/// </summary>
/// <param name="AuthorizationUrl">The fully constructed Keycloak authorization URL to redirect the user to.</param>
/// <param name="StateToken">The cryptographically-random state token (Base64URL, ≥32 bytes) for CSRF protection.</param>
/// <param name="Nonce">The cryptographically-random OIDC nonce (Base64URL, ≥32 bytes) for id_token replay protection.</param>
/// <param name="CodeVerifier">The PKCE code_verifier (43–128 URL-safe chars) for authorization code exchange.</param>
/// <param name="RedirectUri">The exact redirect_uri used in the authorization request (must be reused at code exchange).</param>
public sealed record AuthUrlResult(
    string AuthorizationUrl,
    string StateToken,
    string Nonce,
    string CodeVerifier,
    string RedirectUri);
