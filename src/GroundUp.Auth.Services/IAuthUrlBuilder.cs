using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Builds Keycloak authorization URLs with state, nonce, PKCE, and realm routing.
/// Generates all cryptographic parameters required for secure OAuth2/OIDC flows.
/// </summary>
public interface IAuthUrlBuilder
{
    /// <summary>
    /// Builds a Keycloak authorization URL with state, nonce, PKCE, and realm routing.
    /// </summary>
    /// <param name="request">The URL building request containing realm override and redirect URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A successful result containing the authorization URL, state token, nonce, code verifier, and redirect URI;
    /// or a failure result if required configuration (PublicBaseUrl, AppClientId) is missing.
    /// </returns>
    Task<OperationResult<AuthUrlResult>> BuildAuthorizationUrlAsync(
        AuthUrlRequest request,
        CancellationToken cancellationToken = default);
}
