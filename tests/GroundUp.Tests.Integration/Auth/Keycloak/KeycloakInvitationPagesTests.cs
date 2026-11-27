using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// GU-68 Requirement 7.2 against a real Keycloak: the pages the invitation and join-link flows send
/// people to behave as the design assumes. The authorization URLs come from the real
/// <see cref="AuthUrlBuilderService"/>; a cookie-keeping HttpClient stands in for the browser.
/// <list type="bullet">
/// <item><c>prompt=create</c> opens registration; a new account comes back with a code and an
/// unverified email (the invitation link is the proof of the email).</item>
/// <item><c>login_hint</c> fills in the sign-in page.</item>
/// <item>With a live session, no prompt returns straight away (single sign-on), while
/// <c>prompt=login</c> asks again (the different-account case).</item>
/// </list>
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakInvitationPagesTests
{
    private const string Password = "Invite123!x";
    private readonly KeycloakFixture _fixture;

    public KeycloakInvitationPagesTests(KeycloakFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Registration_NewInvitee_RegistersAndComesBackWithAnUnverifiedEmail()
    {
        var email = $"reg-{Guid.NewGuid():N}@example.com";
        using var browser = Browser();
        var url = await BuildUrlAsync(isSignup: true, loginHint: email);

        var page = await GetFollowingRedirectsAsync(browser, url.AuthorizationUrl);
        var form = Regex.Match(page, "<form[^>]*id=\"kc-register-form\"[^>]*>", RegexOptions.IgnoreCase);
        form.Success.Should().BeTrue("prompt=create opens Keycloak's registration page");

        var fields = new Dictionary<string, string>
        {
            ["email"] = email,
            ["firstName"] = "New",
            ["lastName"] = "Invitee",
            ["password"] = Password,
            ["password-confirm"] = Password
        };
        if (Regex.IsMatch(page, "name=\"username\"", RegexOptions.IgnoreCase))
        {
            fields["username"] = email;
        }

        var response = await browser.PostAsync(FormAction(form.Value), new FormUrlEncodedContent(fields));

        response.StatusCode.Should().Be(HttpStatusCode.Found, await TitleAsync(response));
        var code = CodeFrom(response, url.StateToken);

        var idp = CreateIdentityProviderService();
        var tokens = await idp.ExchangeCodeForTokensAsync(code, KeycloakFixture.AppRedirectUri, codeVerifier: url.CodeVerifier);
        tokens.Should().NotBeNull();
        var userInfo = await idp.GetUserInfoAsync(tokens!.AccessToken);
        userInfo!.Email.Should().Be(email);
        userInfo.Attributes.Should().ContainKey("email_verified")
            .WhoseValue.Should().Be("false", "a new account has not verified its email; the invitation link is the proof");
    }

    [Fact]
    public async Task SignIn_LoginHint_FillsInTheEmail()
    {
        var email = $"hint-{Guid.NewGuid():N}@example.com";
        await CreateUserAsync(email);
        using var browser = Browser();
        var url = await BuildUrlAsync(loginHint: email);

        var page = await GetFollowingRedirectsAsync(browser, url.AuthorizationUrl);

        page.Should().Contain("kc-form-login");
        Regex.Match(page, "<input[^>]*id=\"username\"[^>]*>", RegexOptions.IgnoreCase).Value
            .Should().Contain($"value=\"{email}\"", "login_hint pre-fills the sign-in page");
    }

    [Fact]
    public async Task SignIn_WithALiveSession_NoPromptReturnsAtOnce_PromptLoginAsksAgain()
    {
        var email = $"sso-{Guid.NewGuid():N}@example.com";
        await CreateUserAsync(email);
        using var browser = Browser();

        // First sign-in creates the single sign-on session
        var first = await BuildUrlAsync(loginHint: email);
        var loginPage = await GetFollowingRedirectsAsync(browser, first.AuthorizationUrl);
        var form = Regex.Match(loginPage, "<form[^>]*id=\"kc-form-login\"[^>]*>", RegexOptions.IgnoreCase);
        var signIn = await browser.PostAsync(FormAction(form.Value), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = email,
            ["password"] = Password,
            ["credentialId"] = ""
        }));
        signIn.StatusCode.Should().Be(HttpStatusCode.Found, await TitleAsync(signIn));

        // Same account again, no prompt: straight back with a code (no Keycloak page)
        var again = await BuildUrlAsync(loginHint: email);
        var silent = await browser.GetAsync(again.AuthorizationUrl);
        silent.StatusCode.Should().Be(HttpStatusCode.Found);
        silent.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(KeycloakFixture.AppRedirectUri);

        // A different invited account: prompt=login shows the sign-in page despite the session
        var forced = await BuildUrlAsync(forceLogin: true, loginHint: $"other-{Guid.NewGuid():N}@example.com");
        var forcedPage = await GetFollowingRedirectsAsync(browser, forced.AuthorizationUrl);
        forcedPage.Should().Contain("kc-form-login");
    }

    // ── Helpers ───────────────────────────────────────────────────

    private Task<AuthUrlResult> BuildUrlAsync(bool isSignup = false, bool forceLogin = false, string? loginHint = null)
        => new AuthUrlBuilderService(Options.Create(_fixture.CreateRealmOptions()))
            .BuildAuthorizationUrlAsync(new AuthUrlRequest(null, KeycloakFixture.AppRedirectUri, isSignup, null, forceLogin, loginHint))
            .ContinueWith(t => t.Result.Data!);

    private KeycloakIdentityProviderService CreateIdentityProviderService()
    {
        var factory = _fixture.CreateHttpClientFactory();
        return new KeycloakIdentityProviderService(
            factory, _fixture.CreateRealmOptionsMonitor(), new JwksCache(factory),
            NullLogger<KeycloakIdentityProviderService>.Instance);
    }

    private static HttpClient Browser()
        => new(new CookieJarHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }));

    private static async Task<string> GetFollowingRedirectsAsync(HttpClient browser, string url)
    {
        for (var hop = 0; hop < 5; hop++)
        {
            var response = await browser.GetAsync(url);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.Redirect)
            {
                url = new Uri(new Uri(url), response.Headers.Location!).ToString();
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException("Too many redirects before the Keycloak page.");
    }

    private static string FormAction(string formTag)
    {
        var action = Regex.Match(formTag, "action=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        action.Success.Should().BeTrue("the Keycloak form should have an action");
        return WebUtility.HtmlDecode(action.Groups[1].Value);
    }

    private static string CodeFrom(HttpResponseMessage response, string expectedState)
    {
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(KeycloakFixture.AppRedirectUri);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["state"].ToString().Should().Be(expectedState);
        return query["code"].ToString();
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response)
    {
        var page = await response.Content.ReadAsStringAsync();
        var title = Regex.Match(page, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value.Trim();
        var error = Regex.Match(page, "<span[^>]*(kc-feedback-text|input-error)[^>]*>(.*?)</span>", RegexOptions.Singleline).Groups[2].Value.Trim();
        return $"HTTP {(int)response.StatusCode}, page '{title}' {error}";
    }

    private async Task CreateUserAsync(string email)
    {
        var adminToken = await _fixture.GetAdminTokenAsync();
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var response = await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users",
            new
            {
                username = email,
                email,
                firstName = "Sign",
                lastName = "In",
                emailVerified = true,
                enabled = true,
                requiredActions = Array.Empty<string>(),
                credentials = new[] { new { type = "password", value = Password, temporary = false } }
            });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sends back Keycloak's cookies over plain HTTP, as a browser on localhost would (they are marked
    /// Secure, which HttpClient's own cookie container would drop).
    /// </summary>
    private sealed class CookieJarHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var header in setCookies)
                {
                    var pair = header.Split(';', 2)[0];
                    var separator = pair.IndexOf('=');
                    if (separator > 0)
                    {
                        _cookies[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
                    }
                }
            }

            return response;
        }
    }
}
