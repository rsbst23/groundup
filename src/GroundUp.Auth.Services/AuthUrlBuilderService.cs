using System.Security.Cryptography;
using System.Text;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Builds Keycloak authorization URLs with state, nonce, PKCE, and realm routing.
/// Generates all cryptographic parameters required for secure OAuth2/OIDC flows.
/// </summary>
public sealed class AuthUrlBuilderService : IAuthUrlBuilder
{
    private const int StateTokenByteLength = 32;
    private const int NonceByteLength = 32;
    private const int CodeVerifierByteLength = 32;

    /// <summary>
    /// Unreserved characters allowed in PKCE code_verifier per RFC 7636.
    /// </summary>
    private static readonly char[] UnreservedChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~".ToCharArray();

    private readonly IOptions<KeycloakOptions> _keycloakOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthUrlBuilderService"/>.
    /// </summary>
    /// <param name="keycloakOptions">Keycloak configuration options.</param>
    public AuthUrlBuilderService(IOptions<KeycloakOptions> keycloakOptions)
    {
        _keycloakOptions = keycloakOptions;
    }

    /// <inheritdoc />
    public Task<OperationResult<AuthUrlResult>> BuildAuthorizationUrlAsync(
        AuthUrlRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = _keycloakOptions.Value;

        // Validate required configuration
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            return Task.FromResult(
                OperationResult<AuthUrlResult>.Fail(
                    "KeycloakOptions.PublicBaseUrl is not configured.",
                    500,
                    "MISSING_CONFIGURATION"));
        }

        if (string.IsNullOrWhiteSpace(options.AppClientId))
        {
            return Task.FromResult(
                OperationResult<AuthUrlResult>.Fail(
                    "KeycloakOptions.AppClientId is not configured.",
                    500,
                    "MISSING_CONFIGURATION"));
        }

        // Generate cryptographic parameters
        var stateToken = GenerateBase64UrlToken(StateTokenByteLength);
        var nonce = GenerateBase64UrlToken(NonceByteLength);
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        // Determine realm
        var realm = request.RealmOverride ?? options.SharedRealmName;

        // Build authorization URL
        var baseUrl = options.PublicBaseUrl.TrimEnd('/');
        var authorizationEndpoint = $"{baseUrl}/realms/{realm}/protocol/openid-connect/auth";

        var queryParams = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.AppClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["scope"] = "openid email profile",
            ["state"] = stateToken,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var queryString = string.Join("&", queryParams.Select(
            kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var authorizationUrl = $"{authorizationEndpoint}?{queryString}";

        var result = new AuthUrlResult(
            AuthorizationUrl: authorizationUrl,
            StateToken: stateToken,
            Nonce: nonce,
            CodeVerifier: codeVerifier,
            RedirectUri: request.RedirectUri);

        return Task.FromResult(OperationResult<AuthUrlResult>.Ok(result));
    }

    /// <summary>
    /// Generates a cryptographically random Base64URL-encoded token (no padding).
    /// </summary>
    private static string GenerateBase64UrlToken(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Generates a PKCE code_verifier using cryptographically random URL-safe characters.
    /// Length is 43–128 characters per RFC 7636.
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        // Generate 48 random bytes to produce a verifier in the valid length range.
        // 48 bytes → 64 Base64URL characters (within 43–128 range).
        const int byteLength = 48;
        var bytes = RandomNumberGenerator.GetBytes(byteLength);

        var sb = new StringBuilder(byteLength * 2);
        foreach (var b in bytes)
        {
            sb.Append(UnreservedChars[b % UnreservedChars.Length]);
        }

        // Ensure length is within the 43–128 range (48 bytes → 48 chars with modulo approach).
        // We use exactly 48 characters which is within the valid range.
        return sb.ToString();
    }

    /// <summary>
    /// Computes the PKCE code_challenge from the code_verifier using S256.
    /// code_challenge = Base64URL(SHA256(code_verifier)) without padding.
    /// </summary>
    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var verifierBytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(verifierBytes);
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Encodes bytes as Base64URL without padding.
    /// Base64URL: '+' → '-', '/' → '_', trim '=' padding.
    /// </summary>
    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
