using System.IdentityModel.Tokens.Jwt;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Session;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.TestHelpers;

/// <summary>
/// Shared doubles for the login callback handler tests.
/// </summary>
internal static class FlowHandlerTestDoubles
{
    /// <summary>
    /// An ID token validator that decodes the token without checking the signature and compares the
    /// nonce, so handler tests keep using simple unsigned test tokens. The real signature, issuer,
    /// audience and expiry checks are covered by the Keycloak validator's own tests.
    /// </summary>
    public static IIdTokenValidator DecodingIdTokenValidator() => new DecodingValidator();

    /// <summary>
    /// A session guard over a policy provider that returns <paramref name="absoluteMinutes"/> for the
    /// System level and every tenant (480 minutes by default, so ordinary tests are unaffected).
    /// </summary>
    public static CallbackSessionGuard SessionGuard(int absoluteMinutes = 480) =>
        new(PolicyProvider(absoluteMinutes), NullLogger<CallbackSessionGuard>.Instance);

    /// <summary>A policy provider with the same lifetime everywhere.</summary>
    public static ISessionPolicyProvider PolicyProvider(int absoluteMinutes = 480)
    {
        var policy = new SessionPolicy(
            TimeSpan.FromMinutes(Math.Min(60, absoluteMinutes)),
            TimeSpan.FromMinutes(absoluteMinutes));
        var provider = Substitute.For<ISessionPolicyProvider>();
        provider.GetSystemPolicyAsync(Arg.Any<CancellationToken>()).Returns(policy);
        provider.GetPolicyForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(policy);
        return provider;
    }

    private sealed class DecodingValidator : IIdTokenValidator
    {
        public Task<OperationResult<ValidatedIdToken>> ValidateAsync(
            string? idToken,
            string expectedNonce,
            string? realm,
            CancellationToken cancellationToken = default)
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            if (string.IsNullOrWhiteSpace(idToken) || !handler.CanReadToken(idToken))
            {
                return Task.FromResult(OperationResult<ValidatedIdToken>.Fail("invalid", 401, IdTokenErrorCodes.Invalid));
            }

            var jwt = handler.ReadJwtToken(idToken);
            var nonce = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult<ValidatedIdToken>.Fail("nonce", 400, IdTokenErrorCodes.NonceMismatch));
            }

            DateTimeOffset? authTime = long.TryParse(jwt.Claims.FirstOrDefault(c => c.Type == "auth_time")?.Value, out var s)
                ? DateTimeOffset.FromUnixTimeSeconds(s)
                : null;

            return Task.FromResult(OperationResult<ValidatedIdToken>.Ok(new ValidatedIdToken(jwt.Subject ?? string.Empty, authTime)));
        }
    }
}
