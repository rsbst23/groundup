using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for the SimpleSignup flow (FlowType.SimpleSignup) in an app with
/// <b>no</b> default tenant configured, plus <c>POST /auth/signup</c> routing.
/// <para>
/// Tests that need a configured default tenant (new user auto-joins it) live in
/// <see cref="DefaultTenantFlowTests"/>, which uses its own database so the setting cannot
/// leak into these tests.
/// </para>
/// </summary>
[Collection("AuthFlow")]
public sealed class SimpleSignupFlowTests : AuthFlowTestBase
{
    public SimpleSignupFlowTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. No default tenant configured → NO_DEFAULT_TENANT
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_NoDefaultTenantConfigured_ReturnsNoDefaultTenantError()
    {
        // Arrange — this fixture never configures auth.application.default-tenant-slug
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-nodefault-{suffix}";
        var email = $"nodefault-{suffix}@example.com";

        var nonce = AuthFlowTestFixture.GenerateNonce();
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken,
            nonce: nonce);

        // Code exchange and userinfo succeed — failure happens at the "read setting" step
        SetupFullSuccessfulCallback(sub, nonce, email);

        var request = CreateCallbackRequest("auth-code-nodefault", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — the specific reason, not just "some failure"
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("error").GetString().Should().Be("NO_DEFAULT_TENANT");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Code exchange failure → non-200 response
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_SimpleSignup_CodeExchangeFailure_ReturnsError()
    {
        // Arrange
        var stateToken = AuthFlowTestFixture.GenerateStateToken();

        await CreatePendingFlowStateAsync(
            FlowType.SimpleSignup,
            stateToken: stateToken);

        SetupFailedCodeExchange();

        var request = CreateCallbackRequest("invalid-code-simple", stateToken);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — not a success response
        response.IsSuccessStatusCode.Should().BeFalse(
            "a code exchange failure must return an error response");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Signup endpoint routing — POST /auth/signup with no body → SimpleSignup
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_NullOrganizationName_Returns200WithRedirectUrl()
    {
        // Arrange — POST /auth/signup with no body (organizationName is null → FlowType.SimpleSignup)
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/signup");

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with redirectUrl pointing to Keycloak
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("redirectUrl", out var redirectUrlElement).Should().BeTrue(
            "POST /auth/signup with no organizationName should return a redirectUrl");
        redirectUrlElement.GetString().Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Signup endpoint routing — POST /auth/signup with organizationName → NewOrganization
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_WithOrganizationName_Returns200WithRedirectUrl()
    {
        // Arrange — POST /auth/signup with an organizationName body
        var requestBody = new StringContent(
            """{"organizationName":"Acme Corp"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/signup")
        {
            Content = requestBody
        };

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with redirectUrl (NewOrganization flow initiated)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("redirectUrl", out var redirectUrlElement).Should().BeTrue(
            "POST /auth/signup with organizationName should return a redirectUrl for NewOrganization flow");
        redirectUrlElement.GetString().Should().NotBeNullOrEmpty();
    }
}
