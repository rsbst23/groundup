using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak.Models;
using GroundUp.Auth.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Keycloak implementation of <see cref="IIdentityProviderService"/>.
/// Handles OAuth2 code exchange, JWT validation via JWKS, and userinfo retrieval.
/// Uses the "KeycloakIdp" named HttpClient via <see cref="IHttpClientFactory"/>.
/// </summary>
internal sealed class KeycloakIdentityProviderService : IIdentityProviderService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<KeycloakOptions> _options;
    private readonly JwksCache _jwksCache;
    private readonly ILogger<KeycloakIdentityProviderService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="KeycloakIdentityProviderService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating named clients.</param>
    /// <param name="options">The options monitor providing current Keycloak configuration.</param>
    /// <param name="jwksCache">The per-issuer JWKS key cache.</param>
    /// <param name="logger">The logger instance.</param>
    public KeycloakIdentityProviderService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<KeycloakOptions> options,
        JwksCache jwksCache,
        ILogger<KeycloakIdentityProviderService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _jwksCache = jwksCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TokenResponseDto?> ExchangeCodeForTokensAsync(
        string code,
        string redirectUri,
        string? realm = null,
        string? clientId = null,
        string? codeVerifier = null)
    {
        var opts = _options.CurrentValue;
        var effectiveRealm = realm ?? opts.SharedRealmName;
        var effectiveClientId = clientId ?? opts.AppClientId;

        var tokenUrl = $"{opts.InternalBaseUrl.TrimEnd('/')}/realms/{effectiveRealm}/protocol/openid-connect/token";

        var formParams = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = effectiveClientId
        };

        if (codeVerifier is not null)
        {
            formParams["code_verifier"] = codeVerifier;
        }

        var client = _httpClientFactory.CreateClient("KeycloakIdp");
        var content = new FormUrlEncodedContent(formParams);

        var response = await client.PostAsync(tokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Token exchange returned non-success status {StatusCode} for realm {Realm}",
                (int)response.StatusCode,
                effectiveRealm);
            return null;
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(JsonOptions);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException(
                "Keycloak token endpoint returned HTTP 200 but the response body was malformed or missing access_token.");
        }

        return new TokenResponseDto(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.ExpiresIn,
            tokenResponse.IdToken);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var opts = _options.CurrentValue;
            var expectedPrefix = $"{opts.InternalBaseUrl.TrimEnd('/')}/realms/";

            // Peek at the issuer claim from the token payload without full validation
            var issuer = PeekIssuerFromToken(token);

            if (issuer is null || !issuer.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Token issuer {Issuer} does not match expected prefix {Prefix}", issuer, expectedPrefix);
                return false;
            }

            var signingKeys = await _jwksCache.GetKeysForIssuerAsync(issuer, CancellationToken.None);

            var validationParameters = CreateValidationParameters(signingKeys, expectedPrefix);

            try
            {
                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(token, validationParameters, out _);
                return true;
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                // Unknown kid — refresh JWKS and retry once
                _logger.LogDebug("Unknown signing key for issuer {Issuer}, refreshing JWKS", issuer);
                var refreshedKeys = await _jwksCache.RefreshKeysForIssuerAsync(issuer, CancellationToken.None);
                var retryParams = CreateValidationParameters(refreshedKeys, expectedPrefix);

                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(token, retryParams, out _);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Token validation failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ExternalUserInfo?> GetUserInfoAsync(string accessToken)
    {
        var opts = _options.CurrentValue;
        var userinfoUrl = $"{opts.InternalBaseUrl.TrimEnd('/')}/realms/{opts.SharedRealmName}/protocol/openid-connect/userinfo";

        var client = _httpClientFactory.CreateClient("KeycloakIdp");

        using var request = new HttpRequestMessage(HttpMethod.Get, userinfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Userinfo endpoint returned non-success status {StatusCode}",
                (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);

        if (claims is null)
        {
            _logger.LogWarning("Userinfo response could not be deserialized");
            return null;
        }

        var sub = GetStringClaim(claims, "sub");
        var email = GetStringClaim(claims, "email");

        if (sub is null || email is null)
        {
            _logger.LogWarning("Userinfo response missing required claims (sub or email)");
            return null;
        }

        var displayName = GetStringClaim(claims, "name") ?? GetStringClaim(claims, "preferred_username");

        // Build attributes from remaining claims
        var reservedClaims = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sub", "email", "name", "preferred_username"
        };

        Dictionary<string, string>? attributes = null;

        foreach (var (key, value) in claims)
        {
            if (reservedClaims.Contains(key))
            {
                continue;
            }

            var stringValue = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => value.GetRawText(),
                _ => value.GetRawText()
            };

            if (stringValue is not null)
            {
                attributes ??= new Dictionary<string, string>();
                attributes[key] = stringValue;
            }
        }

        return new ExternalUserInfo(sub, email, displayName, attributes);
    }

    /// <summary>
    /// Peeks at the issuer (iss) claim from the token payload by decoding the base64 middle segment
    /// without performing full JWT validation.
    /// </summary>
    private static string? PeekIssuerFromToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1];

            // Add padding if necessary for base64url decoding
            var remainder = payload.Length % 4;
            var padded = remainder switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var base64 = padded.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64);
            var json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("iss", out var issProp) && issProp.ValueKind == JsonValueKind.String)
            {
                return issProp.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates token validation parameters for JWT validation.
    /// </summary>
    private static TokenValidationParameters CreateValidationParameters(
        ICollection<SecurityKey> signingKeys,
        string expectedIssuerPrefix)
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateLifetime = true,
            ValidateIssuer = true,
            IssuerValidator = (issuer, _, _) =>
            {
                if (issuer.StartsWith(expectedIssuerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return issuer;
                }

                throw new SecurityTokenInvalidIssuerException(
                    $"Token issuer '{issuer}' does not match expected prefix '{expectedIssuerPrefix}'.");
            },
            ValidateAudience = false
        };
    }

    /// <summary>
    /// Extracts a string claim value from the claims dictionary.
    /// </summary>
    private static string? GetStringClaim(Dictionary<string, JsonElement> claims, string claimName)
    {
        if (claims.TryGetValue(claimName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
