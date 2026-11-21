using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Validates the OpenID Connect ID token returned by the identity provider's token endpoint
/// before any of its claims are trusted (OpenID Connect Core 1.0, section 3.1.3.7): signature
/// against the provider's published keys, issuer, audience, expiry and nonce.
/// <para>
/// Identity-provider specific: each provider package (for example GroundUp.Auth.Keycloak)
/// registers its own implementation.
/// </para>
/// </summary>
public interface IIdTokenValidator
{
    /// <summary>
    /// Validates <paramref name="idToken"/> for the given realm and expected nonce.
    /// </summary>
    /// <param name="idToken">The raw ID token from the token response.</param>
    /// <param name="expectedNonce">The nonce stored with the login flow state.</param>
    /// <param name="realm">The realm the login used, or null for the shared realm.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The trusted claims on success. On failure, error code <see cref="IdTokenErrorCodes.NonceMismatch"/>
    /// when only the nonce is wrong, otherwise <see cref="IdTokenErrorCodes.Invalid"/>.
    /// </returns>
    Task<OperationResult<ValidatedIdToken>> ValidateAsync(
        string? idToken,
        string expectedNonce,
        string? realm,
        CancellationToken cancellationToken = default);
}
