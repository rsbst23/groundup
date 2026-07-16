using System.Net;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for auth security controls.
/// Validates Requirements 15.7, 15.8, 15.9, 15.10 — cross-subdomain cookie domain,
/// replay attack protection (410 Gone), state cookie binding rejection, and OIDC nonce validation.
/// </summary>
[Collection("AuthFlow")]
public sealed class SecurityControlsTests : AuthFlowTestBase
{
    public SecurityControlsTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Cross-Subdomain Cookie Domain Attribute (Requirement 15.7)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_Login_CookieDomainAttribute_IsDotPrefixedDefaultDomain()
    {
        // Arrange — trigger a successful Login flow (single membership) that issues a cookie
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-domain-{suffix}";
        var tenantId = await SeedTenantAsync($"Domain Corp {suffix}", $"domain-{suffix}");
        var userId = await SeedUserAsync(sub, $"domain-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        SetupFullSuccessfulCallback(sub, flowState.Nonce, $"domain-{suffix}@example.com");

        var request = CreateCallbackRequest("auth-code-domain", flowState.StateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — the response should be a redirect (successful single-membership auto-select)
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Assert — the Set-Cookie header contains Domain=.testapp.local
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue(
            "successful auth flow should set a cookie");

        var authCookieHeader = cookies!
            .FirstOrDefault(c => c.StartsWith("AuthToken=", StringComparison.OrdinalIgnoreCase));
        authCookieHeader.Should().NotBeNull("AuthToken cookie should be present in Set-Cookie");

        // The domain attribute should be .testapp.local (dot-prefixed default domain)
        authCookieHeader.Should().Contain(
            $"domain=.{AuthFlowTestFixture.TestDefaultDomain}",
            "cookie Domain must be dot-prefixed default domain for cross-subdomain sharing");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Replay Attack → 410 Gone (Requirement 15.8)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_ConsumedState_Returns410Gone()
    {
        // Arrange — create an already-consumed AuthFlowState (simulates replay)
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreateConsumedFlowStateAsync(FlowType.Login, stateToken: stateToken);

        var request = CreateCallbackRequest("auth-code-replay-security", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 410 Gone (replay attack detected)
        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. State Cookie Binding Rejection (Requirement 15.9)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_WithoutStateCookie_IsRejected()
    {
        // Arrange — create a valid pending AuthFlowState
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(FlowType.Login, stateToken: stateToken);

        // Send callback WITHOUT the state cookie
        var request = CreateCallbackRequestWithoutCookie("auth-code-no-cookie", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — rejected (400 or 403 — state cookie binding failure)
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden },
            "callback without state cookie must be rejected as CSRF binding failure");
    }

    [Fact]
    public async Task Callback_WithMismatchedStateCookie_IsRejected()
    {
        // Arrange — create a valid pending AuthFlowState
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(FlowType.Login, stateToken: stateToken);

        // Send callback with a MISMATCHED cookie (cookie value != state query param)
        var request = CreateCallbackRequestWithMismatchedCookie("auth-code-mismatch-cookie", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — rejected (400 or 403 — state cookie mismatch)
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden },
            "callback with mismatched state cookie must be rejected as CSRF binding failure");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Nonce Mismatch Rejection (Requirement 15.10)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_NonceMismatch_ReturnsError()
    {
        // Arrange — create a pending flow state with nonce "A"
        var sub = $"kc-nonce-{Guid.NewGuid():N}";
        var storedNonce = AuthFlowTestFixture.GenerateNonce();
        var wrongNonce = AuthFlowTestFixture.GenerateNonce(); // Different nonce for id_token
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(
            FlowType.Login,
            stateToken: stateToken,
            nonce: storedNonce);

        // Setup code exchange to return id_token with WRONG nonce (wrongNonce != storedNonce)
        SetupSuccessfulCodeExchange(sub, wrongNonce);
        SetupSuccessfulUserInfo(sub);

        var request = CreateCallbackRequest("auth-code-nonce-mismatch", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — error response (not a redirect, not 200)
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect,
            "nonce mismatch must not result in a successful redirect");
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "nonce mismatch must not result in a 200 OK");
        response.IsSuccessStatusCode.Should().BeFalse(
            "nonce mismatch must result in an error status code");
    }
}
