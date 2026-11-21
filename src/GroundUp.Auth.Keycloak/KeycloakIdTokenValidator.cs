using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Validates Keycloak ID tokens (OpenID Connect Core 1.0, section 3.1.3.7):
/// <list type="bullet">
///   <item>Signature against the realm's JWKS (fetched from the internal URL, cached, refreshed once
///   on an unknown key id). Unsigned tokens are rejected.</item>
///   <item>Issuer is the realm at the public or the internal base URL (Keycloak sets <c>iss</c> from
///   its configured hostname or, without one, from the URL the token endpoint was called on).</item>
///   <item>Audience contains the app client id; with several audiences, <c>azp</c> is the client id.</item>
///   <item>Not expired (one minute of clock skew).</item>
///   <item>Nonce equals the one stored with the login flow.</item>
/// </list>
/// Token contents are never logged.
/// </summary>
internal sealed class KeycloakIdTokenValidator : IIdTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly IOptionsMonitor<KeycloakOptions> _options;
    private readonly JwksCache _jwksCache;
    private readonly ILogger<KeycloakIdTokenValidator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="KeycloakIdTokenValidator"/>.
    /// </summary>
    /// <param name="options">Current Keycloak configuration.</param>
    /// <param name="jwksCache">Per-issuer signing key cache.</param>
    /// <param name="logger">Logger.</param>
    public KeycloakIdTokenValidator(
        IOptionsMonitor<KeycloakOptions> options,
        JwksCache jwksCache,
        ILogger<KeycloakIdTokenValidator> logger)
    {
        _options = options;
        _jwksCache = jwksCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ValidatedIdToken>> ValidateAsync(
        string? idToken,
        string expectedNonce,
        string? realm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return Invalid("The token response did not include an ID token.");
        }

        var opts = _options.CurrentValue;
        var effectiveRealm = string.IsNullOrWhiteSpace(realm) ? opts.SharedRealmName : realm;
        var internalIssuer = $"{opts.InternalBaseUrl.TrimEnd('/')}/realms/{effectiveRealm}";
        var publicIssuer = $"{opts.PublicBaseUrl.TrimEnd('/')}/realms/{effectiveRealm}";

        JwtSecurityToken jwt;
        try
        {
            var keys = await _jwksCache.GetKeysForIssuerAsync(internalIssuer, cancellationToken);
            try
            {
                jwt = Validate(idToken, keys, internalIssuer, publicIssuer, opts.AppClientId);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                // Keycloak rotated its keys: refresh once (rate-limited by the cache) and retry.
                var refreshed = await _jwksCache.RefreshKeysForIssuerAsync(internalIssuer, cancellationToken);
                jwt = Validate(idToken, refreshed, internalIssuer, publicIssuer, opts.AppClientId);
            }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or HttpRequestException)
        {
            _logger.LogWarning("ID token rejected for realm {Realm}: {Reason}", effectiveRealm, ex.GetType().Name);
            return Invalid("The ID token could not be validated.");
        }

        var nonce = jwt.Payload.TryGetValue("nonce", out var nonceValue) ? nonceValue?.ToString() : null;
        if (nonce is null || !FixedTimeEquals(nonce, expectedNonce))
        {
            return OperationResult<ValidatedIdToken>.Fail(
                "ID token nonce does not match the expected value.", 400, IdTokenErrorCodes.NonceMismatch);
        }

        return OperationResult<ValidatedIdToken>.Ok(new ValidatedIdToken(jwt.Subject, ReadAuthTime(jwt)));
    }

    private static JwtSecurityToken Validate(
        string idToken,
        ICollection<SecurityKey> keys,
        string internalIssuer,
        string publicIssuer,
        string clientId)
    {
        var parameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidIssuers = new[] { internalIssuer, publicIssuer },
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew
        };

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(idToken, parameters, out var validated);
        var jwt = (JwtSecurityToken)validated;

        // OIDC Core 3.1.3.7 (4, 5): with several audiences, the authorized party must be this client.
        if (jwt.Audiences.Count() > 1 && !string.Equals(jwt.Payload.Azp, clientId, StringComparison.Ordinal))
        {
            throw new SecurityTokenInvalidAudienceException("ID token azp does not match the client.");
        }

        if (string.IsNullOrEmpty(jwt.Subject))
        {
            throw new SecurityTokenValidationException("ID token has no subject.");
        }

        return jwt;
    }

    private static DateTimeOffset? ReadAuthTime(JwtSecurityToken jwt)
    {
        if (!jwt.Payload.TryGetValue("auth_time", out var value) || value is null)
        {
            return null;
        }

        return long.TryParse(value.ToString(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static OperationResult<ValidatedIdToken> Invalid(string message) =>
        OperationResult<ValidatedIdToken>.Fail(message, 401, IdTokenErrorCodes.Invalid);
}
