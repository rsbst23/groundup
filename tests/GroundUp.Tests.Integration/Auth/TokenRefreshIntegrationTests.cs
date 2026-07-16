using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for sliding token refresh and absolute session lifetime cap.
/// Validates Requirements 15.11 and 15.12 — the <c>TokenRefreshMiddleware</c> reissues
/// tokens when they pass 50% of their lifetime, and stops when the absolute session cap
/// is exceeded.
/// </summary>
[Collection("AuthFlow")]
public sealed class TokenRefreshIntegrationTests : AuthFlowTestBase
{
    public TokenRefreshIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Sliding refresh — token past 50% gets refreshed
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticatedRequest_TokenPast50Percent_RefreshesWithNewCookie()
    {
        // Arrange — seed a user with an active membership
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Refresh Corp", $"refresh-{suffix}");
        var externalSub = $"kc-refresh-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"refresh-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Generate a token with iat set to 35 minutes ago (> 50% of 60-min expiration)
        // and auth_time set to 35 minutes ago (well within 480-min absolute cap)
        var iatTime = DateTimeOffset.UtcNow.AddMinutes(-35);
        var authTime = iatTime; // Same as iat for initial issuance
        var token = GenerateTokenWithTimestamps(userId, tenantId, iatTime, authTime);

        // Act — send an authenticated request with the aged token
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", token);
        var response = await Client.SendAsync(request);

        // Assert — should succeed and contain a Set-Cookie with a new AuthToken
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var newAuthCookie = ExtractAuthCookie(response);
        newAuthCookie.Should().NotBeNullOrEmpty(
            "the middleware should refresh the token and set a new cookie when token age > 50%");

        // The new token should be different from the original
        newAuthCookie.Should().NotBe(token, "the refreshed token must be a new token");

        // Validate the new token still has correct claims
        var handler = new JwtSecurityTokenHandler();
        var newJwt = handler.ReadJwtToken(newAuthCookie);

        var tidClaim = newJwt.Claims.FirstOrDefault(c => c.Type == "tid");
        tidClaim.Should().NotBeNull();
        tidClaim!.Value.Should().Be(tenantId.ToString());

        var subClaim = newJwt.Claims.FirstOrDefault(c => c.Type == "sub");
        subClaim.Should().NotBeNull();
        subClaim!.Value.Should().Be(userId.ToString());

        // The auth_time should be preserved from the original token
        var newAuthTimeClaim = newJwt.Claims.FirstOrDefault(c => c.Type == "auth_time");
        newAuthTimeClaim.Should().NotBeNull("auth_time must be preserved on refresh");
        var newAuthTimeUnix = long.Parse(newAuthTimeClaim!.Value);
        newAuthTimeUnix.Should().Be(authTime.ToUnixTimeSeconds(),
            "the original auth_time must be preserved during refresh");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: Absolute session cap — token past cap is NOT refreshed
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticatedRequest_PastAbsoluteSessionCap_DoesNotRefreshToken()
    {
        // Arrange — seed a user with an active membership
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Expired Corp", $"expired-{suffix}");
        var externalSub = $"kc-expired-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"expired-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Generate a token with:
        // - auth_time set to 500 minutes ago (> 480-min absolute cap)
        // - iat set to 35 minutes ago (> 50% of 60-min — would normally trigger refresh)
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-500);
        var iatTime = DateTimeOffset.UtcNow.AddMinutes(-35);
        var token = GenerateTokenWithTimestamps(userId, tenantId, iatTime, authTime);

        // Act — send an authenticated request with this expired-session token
        var request = CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", token);
        var response = await Client.SendAsync(request);

        // Assert — the token is still valid for this request (it hasn't actually expired
        // as a JWT — it's still within TokenExpirationMinutes), but no refresh should happen.
        // The response should NOT contain a Set-Cookie with a new AuthToken.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the token JWT is still valid, but the session has exceeded the absolute cap");

        var newAuthCookie = ExtractAuthCookie(response);
        newAuthCookie.Should().BeNullOrEmpty(
            "the middleware must NOT refresh when auth_time exceeds the absolute session cap — " +
            "the user must re-authenticate through Keycloak");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a GroundUp JWT with explicit <c>iat</c> and <c>auth_time</c> timestamps,
    /// bypassing the fixture's <see cref="AuthFlowTestFixture.GenerateTestToken"/> which
    /// always sets <c>iat</c> to the current time. Includes the <c>kid</c> header required
    /// by <c>TokenService.ValidateTokenAsync</c>.
    /// </summary>
    private static string GenerateTokenWithTimestamps(
        Guid userId,
        Guid tenantId,
        DateTimeOffset issuedAt,
        DateTimeOffset authTime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthFlowTestFixture.TestSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tid", tenantId.ToString()),
            new("email", "test@example.com"),
            new("auth_time", authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = "GroundUp",
            Audience = "GroundUp",
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = DateTime.UtcNow.AddMinutes(60),
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        token.Header["kid"] = "default";

        return tokenHandler.WriteToken(token);
    }
}
