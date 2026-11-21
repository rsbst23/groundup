using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// CSRF protection on the cookie path, end to end through the real middleware and
/// antiforgery services. <c>POST /auth/refresh</c> is the state-changing target.
/// </summary>
[Collection("AuthFlow")]
public sealed class CsrfProtectionIntegrationTests : AuthFlowTestBase
{
    public CsrfProtectionIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Post_CookieAuthWithoutCsrfToken_Returns403CsrfValidationFailed()
    {
        // Arrange
        var token = await CreateSessionTokenAsync("nocsrf");

        // Act
        var response = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Post, "/auth/refresh", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(response)).Should().Be("CSRF_VALIDATION_FAILED");
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task Post_CookieAuthWithValidCsrfToken_Succeeds()
    {
        // Arrange
        var token = await CreateSessionTokenAsync("csrf-ok");
        var request = await CreateCookieRequestWithCsrfAsync(HttpMethod.Post, "/auth/refresh", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ExtractAuthCookie(response).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_CookieAuthWithHeaderButNoCsrfCookie_Returns403()
    {
        // Arrange: the request token alone is not enough; it must match the antiforgery cookie
        var token = await CreateSessionTokenAsync("nocookie");
        var (requestToken, _) = await GetCsrfTokenAsync(token);
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/auth/refresh", token);
        request.Headers.Add("X-CSRF-Token", requestToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_CsrfTokenIssuedToAnotherUser_Returns403()
    {
        // Arrange: Alice's token pair replayed with Bob's session
        var aliceToken = await CreateSessionTokenAsync("alice");
        var bobToken = await CreateSessionTokenAsync("bob");
        var (requestToken, cookieToken) = await GetCsrfTokenAsync(aliceToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"AuthToken={bobToken}; CsrfToken={cookieToken}");
        request.Headers.Add("X-CSRF-Token", requestToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Post_BearerAuthWithoutCsrfToken_Succeeds()
    {
        // Arrange
        var token = await CreateSessionTokenAsync("bearer");

        // Act
        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Post, "/auth/refresh", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_CookieAuthWithoutCsrfToken_IsNotBlocked()
    {
        // Arrange
        var token = await CreateSessionTokenAsync("get");

        // Act
        var response = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/auth/me", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_StaleCookieWhileSignedOut_IsNotBlockedByCsrf()
    {
        // Arrange: an invalid cookie still in the browser must not stop a new sign-in
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/auth/login", "not-a-valid-token");

        // Act
        var response = await Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCsrf_Unauthenticated_Returns401()
    {
        // Act
        var response = await Client.GetAsync("/auth/csrf");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCsrf_Authenticated_ReturnsTokenAndHttpOnlyStrictCookieWithNoStore()
    {
        // Arrange
        var token = await CreateSessionTokenAsync("csrf-get");

        // Act
        var response = await Client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/auth/csrf", token));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("CsrfToken=", StringComparison.Ordinal))
            .ToLowerInvariant();
        cookie.Should().Contain("httponly");
        cookie.Should().Contain("samesite=strict");
        cookie.Should().Contain("path=/");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private async Task<string> CreateSessionTokenAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"CSRF {label} {suffix}", $"csrf-{label}-{suffix}");
        var userId = await SeedUserAsync($"sub-csrf-{label}-{suffix}", $"csrf-{label}-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        return Fixture.GenerateTestToken(
            userId,
            tenantId,
            additionalClaims: new[]
            {
                new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            });
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }
}
