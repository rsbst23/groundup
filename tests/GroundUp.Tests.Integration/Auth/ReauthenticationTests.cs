using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using NSubstitute;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// The absolute session lifetime is counted from the identity provider's <c>auth_time</c>, a silent
/// single sign-on past it is refused with <c>REAUTH_REQUIRED</c>, and login URLs carry
/// <c>max_age</c> (and <c>prompt=login</c> when the client forces a login).
/// </summary>
[Collection("AuthFlow")]
public sealed class ReauthenticationTests : AuthFlowTestBase
{
    public ReauthenticationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Callback_SilentLoginWithinLifetime_TokenKeepsIdentityProviderAuthTime()
    {
        // Arrange — Keycloak's SSO session started 3 hours ago (default lifetime: 8 hours)
        var (externalSub, flowState) = await SeedSingleMemberLoginAsync("silent");
        var authTime = DateTimeOffset.UtcNow.AddHours(-3);
        SetupSuccessfulCodeExchange(externalSub, flowState.Nonce, authTime: authTime);
        SetupSuccessfulUserInfo(externalSub);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("code-silent", flowState.StateToken));

        // Assert — the session is not restarted by the silent sign-on
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ExtractAuthCookie(response));
        jwt.Claims.First(c => c.Type == "auth_time").Value.Should().Be(authTime.ToUnixTimeSeconds().ToString());
    }

    [Fact]
    public async Task Callback_SilentLoginPastLifetime_Returns401ReauthRequiredAndNoCookie()
    {
        // Arrange — Keycloak's SSO session started 9 hours ago; the lifetime is 8 hours
        var (externalSub, flowState) = await SeedSingleMemberLoginAsync("stale");
        SetupSuccessfulCodeExchange(externalSub, flowState.Nonce, authTime: DateTimeOffset.UtcNow.AddHours(-9));
        SetupSuccessfulUserInfo(externalSub);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("code-stale", flowState.StateToken));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("errorCode").GetString().Should().Be("REAUTH_REQUIRED");
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task Callback_IdTokenSignedWithWrongKey_IsRejectedAndNoCookie()
    {
        // Arrange — an ID token that does not verify must never be trusted
        var (externalSub, flowState) = await SeedSingleMemberLoginAsync("forged");
        SetupSuccessfulCodeExchange(externalSub, flowState.Nonce);
        var forged = AuthFlowTestFixture.GenerateFakeIdToken(externalSub, flowState.Nonce)[..^4] + "AAAA";
        Fixture.MockIdentityProviderService
            .ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Task.FromResult<TokenResponseDto?>(new TokenResponseDto("access", "refresh", 300, forged)));
        SetupSuccessfulUserInfo(externalSub);

        // Act
        var response = await Client.SendAsync(CreateCallbackRequest("code-forged", flowState.StateToken));

        // Assert
        response.IsSuccessStatusCode.Should().BeFalse();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("errorCode").GetString().Should().Be("ID_TOKEN_INVALID");
        ExtractAuthCookie(response).Should().BeNull();
    }

    [Fact]
    public async Task Login_RedirectUrlCarriesMaxAgeAndNoPrompt()
    {
        // Act
        var response = await Client.PostAsync("/auth/login", null);

        // Assert — max_age is the absolute session lifetime (default 480 minutes)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = await ReadRedirectQueryAsync(response);
        query["max_age"].Should().Be((480 * 60).ToString());
        query["prompt"].Should().BeNull();
    }

    [Fact]
    public async Task Login_ForceLogin_RedirectUrlCarriesPromptLogin()
    {
        // Act — what a client does after REAUTH_REQUIRED
        var response = await Client.PostAsync("/auth/login?forceLogin=true", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var query = await ReadRedirectQueryAsync(response);
        query["prompt"].Should().Be("login");
        query["max_age"].Should().NotBeNull();
    }

    [Fact]
    public async Task Signup_RedirectUrlKeepsPromptCreate()
    {
        // Act
        var response = await Client.PostAsync(
            "/auth/signup",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        // Assert — login/signup/logout still return a redirectUrl for the client to follow
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadRedirectQueryAsync(response))["prompt"].Should().Be("create");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private async Task<(string ExternalSub, GroundUp.Auth.Core.Entities.AuthFlowState FlowState)> SeedSingleMemberLoginAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Reauth {label} {suffix}", $"reauth-{label}-{suffix}");
        var externalSub = $"kc-reauth-{label}-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"reauth-{label}-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var flowState = await CreatePendingFlowStateAsync(FlowType.Login);
        return (externalSub, flowState);
    }

    private static async Task<System.Collections.Specialized.NameValueCollection> ReadRedirectQueryAsync(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var url = new Uri(json.GetProperty("redirectUrl").GetString()!);
        return System.Web.HttpUtility.ParseQueryString(url.Query);
    }
}
