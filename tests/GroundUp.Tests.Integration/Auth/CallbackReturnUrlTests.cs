using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-87 through the HTTP pipeline: with a return URL the callback redirects to the client's page
/// with the outcome; without one it answers JSON as before.
/// </summary>
[Collection("AuthFlow")]
public sealed class CallbackReturnUrlTests : AuthFlowTestBase
{
    public CallbackReturnUrlTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Callback_LoginWithReturnUrl_RedirectsSignedInAndSetsTheCookie()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Return Corp", $"return-{suffix}");
        var sub = $"kc-return-{suffix}";
        var userId = await SeedUserAsync(sub, $"return-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        var state = await CreatePendingFlowStateAsync(FlowType.Login, returnUrl: "/session");
        SetupFullSuccessfulCallback(sub, state.Nonce, $"return-{suffix}@example.com");

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-return", state.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?result=signed_in");
        ExtractAuthCookie(response).Should().NotBeNullOrEmpty("the cookie is set before the redirect");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("eyJ", "the token is not in the body or the URL");
    }

    [Fact]
    public async Task Callback_NoMembershipsWithReturnUrl_RedirectsNoTenant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sub = $"kc-none-{suffix}";
        await SeedUserAsync(sub, $"none-{suffix}@example.com");
        var state = await CreatePendingFlowStateAsync(FlowType.Login, returnUrl: "/session");
        SetupFullSuccessfulCallback(sub, state.Nonce, $"none-{suffix}@example.com");

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-none", state.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?result=no_tenant");
    }

    [Fact]
    public async Task Callback_ReplayedStateWithReturnUrl_RedirectsWithTheCode()
    {
        var state = await CreateConsumedFlowStateAsync(FlowType.Login);
        await SetReturnUrlAsync(state.Id, "/session");

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-replay", state.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?error=FLOW_ALREADY_CONSUMED");
    }

    [Fact]
    public async Task Callback_StateCookieMismatchWithReturnUrl_RedirectsWithTheCodeAndLeavesTheStatePending()
    {
        var state = await CreatePendingFlowStateAsync(FlowType.Login, returnUrl: "/session");

        var response = await Client.SendAsync(CreateCallbackRequestWithMismatchedCookie("auth-code", state.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?error=CSRF_STATE_MISMATCH");
        (await GetFlowStateByStateTokenAsync(state.StateToken))!.Status.Should().Be(FlowStatus.Pending);
    }

    [Fact]
    public async Task Callback_WithoutReturnUrl_AnswersJsonAsBefore()
    {
        var state = await CreateConsumedFlowStateAsync(FlowType.Login);

        var response = await Client.SendAsync(CreateCallbackRequest("auth-code-replay", state.StateToken));

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await ParseErrorResponseAsync(response))!.ErrorCode.Should().Be("FLOW_ALREADY_CONSUMED");
    }

    [Fact]
    public async Task Callback_ProviderErrorWithReturnUrl_RedirectsAndFailsTheState()
    {
        var state = await CreatePendingFlowStateAsync(FlowType.Login, returnUrl: "/session");
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/auth/callback?error=access_denied&error_description=User%20cancelled&state={Uri.EscapeDataString(state.StateToken)}");
        request.Headers.Add("Cookie", $"AuthState={state.StateToken}");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?error=IDP_ERROR&idpError=access_denied");
        (await GetFlowStateByStateTokenAsync(state.StateToken))!.Status.Should().Be(FlowStatus.Failed);
    }

    [Fact]
    public async Task Callback_ProviderErrorWithoutReturnUrl_Answers400JsonWithIdpError()
    {
        var state = await CreatePendingFlowStateAsync(FlowType.Login);
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/auth/callback?error=access_denied&state={Uri.EscapeDataString(state.StateToken)}");
        request.Headers.Add("Cookie", $"AuthState={state.StateToken}");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("errorCode").GetString().Should().Be(AuthErrorCodes.IdpError);
    }

    [Fact]
    public async Task Login_InvalidReturnUrl_RefusedWithoutAFlowState()
    {
        await using var before = Fixture.CreateAuthDbContext();
        var countBefore = await before.AuthFlowStates.CountAsync();

        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/login?returnUrl=https%3A%2F%2Fevil.example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("errorCode").GetString().Should().Be(AuthErrorCodes.InvalidReturnUrl);
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse("no state cookie is set");
        await using var after = Fixture.CreateAuthDbContext();
        (await after.AuthFlowStates.CountAsync()).Should().Be(countBefore);
    }

    [Fact]
    public async Task Login_ValidReturnUrl_StoredOnTheFlowState()
    {
        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/login?returnUrl=%2Fsession%3Ftab%3D2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var redirectUrl = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString()!;
        var stateToken = Uri.UnescapeDataString(redirectUrl.Split("state=")[1].Split('&')[0]);
        (await GetFlowStateByStateTokenAsync(stateToken))!.ReturnUrl.Should().Be("/session?tab=2");
    }

    private async Task SetReturnUrlAsync(Guid flowStateId, string returnUrl)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var entity = await context.AuthFlowStates.SingleAsync(s => s.Id == flowStateId);
        entity.ReturnUrl = returnUrl;
        await context.SaveChangesAsync();
    }
}
