using System.IdentityModel.Tokens.Jwt;
using System.Text;
using GroundUp.Auth.Services;
using GroundUp.Core.Results;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// ID token validator for the auth integration tests. The real Keycloak validator fetches the
/// realm's JWKS; these tests have no Keycloak, so tokens from
/// <see cref="AuthFlowTestFixture.GenerateFakeIdToken"/> are signed with a test key and checked
/// here the same way: signature, issuer, audience, expiry and nonce.
/// </summary>
internal sealed class TestIdTokenValidator : IIdTokenValidator
{
    /// <summary>The HMAC key the fixture signs test ID tokens with.</summary>
    public const string SigningKey = "fake-key-for-id-token-testing-minimum-32-bytes!";

    public Task<OperationResult<ValidatedIdToken>> ValidateAsync(
        string? idToken,
        string expectedNonce,
        string? realm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return Task.FromResult(OperationResult<ValidatedIdToken>.Fail("missing", 401, IdTokenErrorCodes.Invalid));
        }

        JwtSecurityToken jwt;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            handler.ValidateToken(idToken, new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidIssuer = "http://keycloak/realms/groundup",
                ValidAudience = "groundup-app",
                ValidateLifetime = true,
                RequireSignedTokens = true
            }, out var validated);
            jwt = (JwtSecurityToken)validated;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return Task.FromResult(OperationResult<ValidatedIdToken>.Fail("invalid", 401, IdTokenErrorCodes.Invalid));
        }

        var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            return Task.FromResult(OperationResult<ValidatedIdToken>.Fail("nonce", 400, IdTokenErrorCodes.NonceMismatch));
        }

        DateTimeOffset? authTime = long.TryParse(jwt.Claims.FirstOrDefault(c => c.Type == "auth_time")?.Value, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;

        return Task.FromResult(OperationResult<ValidatedIdToken>.Ok(new ValidatedIdToken(jwt.Subject, authTime)));
    }
}
