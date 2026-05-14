using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Abstraction for external identity provider operations (e.g., Keycloak, Auth0, Azure AD).
/// Defines the contract for OAuth2/OIDC code exchange, token validation, and user info retrieval.
/// This is a stub interface — Phase 10 implements against Keycloak.
/// </summary>
public interface IIdentityProviderService
{
    /// <summary>
    /// Exchanges an authorization code for tokens from the external identity provider.
    /// </summary>
    /// <param name="code">The authorization code received from the OAuth2 callback.</param>
    /// <param name="redirectUri">The redirect URI used in the original authorization request.</param>
    /// <param name="realm">The optional realm/tenant identifier for multi-tenant identity providers.</param>
    /// <returns>The token response containing access token, refresh token, and ID token; or null on failure.</returns>
    Task<TokenResponseDto?> ExchangeCodeForTokensAsync(string code, string redirectUri, string? realm = null);

    /// <summary>
    /// Validates a token issued by the external identity provider.
    /// </summary>
    /// <param name="token">The token string to validate.</param>
    /// <returns>True if the token is valid; otherwise false.</returns>
    Task<bool> ValidateTokenAsync(string token);

    /// <summary>
    /// Retrieves user information from the external identity provider using an access token.
    /// </summary>
    /// <param name="accessToken">A valid access token for the identity provider's userinfo endpoint.</param>
    /// <returns>The external user information if available; otherwise null.</returns>
    Task<ExternalUserInfo?> GetUserInfoAsync(string accessToken);
}
