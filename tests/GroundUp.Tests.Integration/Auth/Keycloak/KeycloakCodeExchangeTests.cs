using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// Authorization-code exchange against a real Keycloak container: a real code is obtained by signing
/// in through Keycloak's login form (with PKCE), then exchanged by
/// <see cref="KeycloakIdentityProviderService.ExchangeCodeForTokensAsync"/>.
/// The redirect URI is never contacted; the code is read from the redirect's Location header, so the
/// test needs no listener and no fixed port.
/// Validates: Phase 10b Requirements 5.1, 5.3, 5.6, 15.2
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakCodeExchangeTests
{
    private const string RedirectUri = KeycloakFixture.AppRedirectUri;
    private const string Password = "CodeFlow123!";

    private readonly KeycloakFixture _fixture;

    public KeycloakCodeExchangeTests(KeycloakFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExchangeCodeForTokensAsync_RealCodeWithPkce_ReturnsValidTokens()
    {
        // Arrange
        var email = $"codeflow-{Guid.NewGuid():N}@example.com";
        var userId = await CreateUserAsync(email);
        var (verifier, challenge) = CreatePkcePair();
        var nonce = Guid.NewGuid().ToString("N");
        var code = await SignInAndGetCodeAsync(email, challenge, nonce);
        var service = CreateService();

        // Act
        var tokens = await service.ExchangeCodeForTokensAsync(code, RedirectUri, codeVerifier: verifier);

        // Assert
        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().NotBeNullOrEmpty();
        tokens.RefreshToken.Should().NotBeNullOrEmpty();
        tokens.ExpiresIn.Should().BePositive();
        (await service.ValidateTokenAsync(tokens.AccessToken)).Should().BeTrue();

        tokens.IdToken.Should().NotBeNullOrEmpty();
        var idToken = ReadJwtPayload(tokens.IdToken!);
        idToken.GetProperty("nonce").GetString().Should().Be(nonce);
        idToken.GetProperty("sub").GetString().Should().Be(userId);
        idToken.GetProperty("email").GetString().Should().Be(email);
    }

    [Fact]
    public async Task ExchangeCodeForTokensAsync_ReusedCode_ReturnsNullWithoutThrowing()
    {
        // Arrange
        var email = $"codereuse-{Guid.NewGuid():N}@example.com";
        await CreateUserAsync(email);
        var (verifier, challenge) = CreatePkcePair();
        var code = await SignInAndGetCodeAsync(email, challenge, Guid.NewGuid().ToString("N"));
        var service = CreateService();
        (await service.ExchangeCodeForTokensAsync(code, RedirectUri, codeVerifier: verifier)).Should().NotBeNull();

        // Act: an authorization code is single-use
        var second = await service.ExchangeCodeForTokensAsync(code, RedirectUri, codeVerifier: verifier);

        // Assert
        second.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCodeForTokensAsync_WrongCodeVerifier_ReturnsNull()
    {
        // Arrange
        var email = $"codepkce-{Guid.NewGuid():N}@example.com";
        await CreateUserAsync(email);
        var (_, challenge) = CreatePkcePair();
        var code = await SignInAndGetCodeAsync(email, challenge, Guid.NewGuid().ToString("N"));
        var (otherVerifier, _) = CreatePkcePair();

        // Act
        var tokens = await CreateService().ExchangeCodeForTokensAsync(code, RedirectUri, codeVerifier: otherVerifier);

        // Assert
        tokens.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCodeForTokensAsync_InvalidCode_ReturnsNullWithoutThrowing()
    {
        var tokens = await CreateService().ExchangeCodeForTokensAsync("not-a-real-code", RedirectUri);

        tokens.Should().BeNull();
    }

    private KeycloakIdentityProviderService CreateService()
    {
        var factory = _fixture.CreateHttpClientFactory();
        return new KeycloakIdentityProviderService(
            factory,
            _fixture.CreateRealmOptionsMonitor(),
            new JwksCache(factory),
            NullLogger<KeycloakIdentityProviderService>.Instance);
    }

    /// <summary>
    /// Signs in through the login form and returns the authorization code from the redirect.
    /// </summary>
    private async Task<string> SignInAndGetCodeAsync(string email, string codeChallenge, string nonce)
    {
        var state = Guid.NewGuid().ToString("N");

        // Keycloak marks its session cookies Secure. Browsers still send those to http://localhost,
        // but HttpClient's CookieContainer does not over plain HTTP, and the test container serves HTTP
        // only. A minimal cookie jar sends them back the way a browser on localhost would.
        using var browser = new HttpClient(new CookieJarHandler(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        }));

        var authorizeUrl = QueryHelpers.AddQueryString(
            $"{_fixture.BaseUrl}/realms/{KeycloakFixture.TestRealmName}/protocol/openid-connect/auth",
            new Dictionary<string, string?>
            {
                ["client_id"] = KeycloakFixture.AppClientId,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["redirect_uri"] = RedirectUri,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            });

        // The authorize endpoint renders the login page, possibly after redirects within Keycloak
        var loginPage = await GetFollowingRedirectsAsync(browser, authorizeUrl);
        var formAction = ExtractLoginFormAction(loginPage);

        var loginResponse = await browser.PostAsync(formAction, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = email,
            ["password"] = Password,
            ["credentialId"] = ""
        }));

        if (loginResponse.StatusCode != HttpStatusCode.Found)
        {
            var page = await loginResponse.Content.ReadAsStringAsync();
            var title = Regex.Match(page, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value.Trim();
            throw new InvalidOperationException(
                $"Login did not redirect (HTTP {(int)loginResponse.StatusCode}, page '{title}').");
        }

        var location = loginResponse.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["state"].ToString().Should().Be(state, "Keycloak must return the state it was given");
        query.Should().ContainKey("code");
        return query["code"].ToString();
    }

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

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the authorize endpoint should render the login page");
            return await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException("Too many redirects before the login page.");
    }

    private static string ExtractLoginFormAction(string html)
    {
        var form = Regex.Match(html, "<form[^>]*id=\"kc-form-login\"[^>]*>", RegexOptions.IgnoreCase);
        form.Success.Should().BeTrue("the Keycloak login page should contain the kc-form-login form");
        var action = Regex.Match(form.Value, "action=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        action.Success.Should().BeTrue("the login form should have an action");
        return WebUtility.HtmlDecode(action.Groups[1].Value);
    }

    /// <summary>
    /// Stores cookies from Set-Cookie headers by name and sends them on every later request, regardless
    /// of the Secure attribute. Test-only: the browser stand-in talks to one host.
    /// </summary>
    private sealed class CookieJarHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
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

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonElement ReadJwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    /// <summary>
    /// Creates a user with a complete profile (so Keycloak shows no update-profile step) and returns its id.
    /// </summary>
    private async Task<string> CreateUserAsync(string email)
    {
        var adminToken = await _fixture.GetAdminTokenAsync();
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsJsonAsync(
            $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users",
            new
            {
                username = email,
                email,
                firstName = "Code",
                lastName = "Flow",
                emailVerified = true,
                enabled = true,
                requiredActions = Array.Empty<string>(),
                credentials = new[] { new { type = "password", value = Password, temporary = false } }
            });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"User creation failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        return response.Headers.Location!.Segments.Last();
    }
}
