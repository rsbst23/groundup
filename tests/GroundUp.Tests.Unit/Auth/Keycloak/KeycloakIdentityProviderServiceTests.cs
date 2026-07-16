using System.Net;
using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Property-based and standard tests for <see cref="KeycloakIdentityProviderService"/>.
/// Feature: phase-10b-keycloak-provider
/// Properties 4, 5, 6, 9
/// Validates: Requirements 5.1, 5.3, 5.4, 5.5, 5.6, 6.3, 6.4, 6.5, 7.2
/// </summary>
[Trait("Category", "Property")]
public sealed class KeycloakIdentityProviderServiceTests
{
    private static readonly KeycloakOptions DefaultOptions = new()
    {
        InternalBaseUrl = "http://keycloak:8080",
        SharedRealmName = "groundup",
        AdminClientId = "admin-cli",
        AdminClientSecret = "secret",
        PublicBaseUrl = "https://keycloak.example.com",
        AppClientId = "groundup-app"
    };

    private static (KeycloakIdentityProviderService service, IdpMockHandler handler) CreateService(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? responseBody = null)
    {
        var handler = new IdpMockHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://keycloak:8080") };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("KeycloakIdp").Returns(httpClient);

        var optionsMonitor = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        optionsMonitor.CurrentValue.Returns(DefaultOptions);

        var jwksCache = new JwksCache(httpClientFactory);
        var logger = NullLogger<KeycloakIdentityProviderService>.Instance;

        var service = new KeycloakIdentityProviderService(httpClientFactory, optionsMonitor, jwksCache, logger);
        return (service, handler);
    }

    /// <summary>
    /// Property 4: Token Endpoint Request Construction.
    /// For any code, redirectUri, realm, clientId, and codeVerifier, the request is sent to the
    /// correct URL and includes the expected form-encoded parameters.
    /// **Validates: Requirements 5.1, 5.5, 5.6**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(IdpServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 4: Token endpoint request construction")]
    public Property ExchangeCode_RequestConstruction_IsCorrect(TokenExchangeInput input)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "at-123",
            refresh_token = "rt-456",
            expires_in = 300,
            id_token = "idt-789"
        });

        var (service, handler) = CreateService(HttpStatusCode.OK, responseJson);

        service.ExchangeCodeForTokensAsync(
            input.Code, input.RedirectUri, input.Realm, input.ClientId, input.CodeVerifier)
            .GetAwaiter().GetResult();

        var request = handler.LastRequest!;
        var requestBody = handler.LastRequestBody!;

        // Verify URL contains the effective realm
        var effectiveRealm = input.Realm ?? DefaultOptions.SharedRealmName;
        var expectedUrlPart = $"/realms/{effectiveRealm}/protocol/openid-connect/token";
        var urlCorrect = request.RequestUri!.ToString().Contains(expectedUrlPart);

        // Verify form params
        var bodyContainsCode = requestBody.Contains($"code={Uri.EscapeDataString(input.Code)}");
        var bodyContainsGrantType = requestBody.Contains("grant_type=authorization_code");

        var effectiveClientId = input.ClientId ?? DefaultOptions.AppClientId;
        var bodyContainsClient = requestBody.Contains($"client_id={Uri.EscapeDataString(effectiveClientId)}");

        // Code verifier included only when provided
        var codeVerifierCorrect = input.CodeVerifier is null
            ? !requestBody.Contains("code_verifier=")
            : requestBody.Contains($"code_verifier={Uri.EscapeDataString(input.CodeVerifier)}");

        return (urlCorrect && bodyContainsCode && bodyContainsGrantType && bodyContainsClient && codeVerifierCorrect)
            .ToProperty();
    }

    /// <summary>
    /// Property 5: Token Response Mapping.
    /// For any valid 200 response containing access_token, refresh_token, expires_in, and id_token,
    /// the result is mapped correctly to TokenResponseDto.
    /// **Validates: Requirements 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(IdpServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 5: Token response mapping")]
    public Property ExchangeCode_ValidResponse_MapsToDto(TokenResponsePayload payload)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = payload.AccessToken,
            refresh_token = payload.RefreshToken,
            expires_in = payload.ExpiresIn,
            id_token = payload.IdToken
        });

        var (service, _) = CreateService(HttpStatusCode.OK, responseJson);

        var result = service.ExchangeCodeForTokensAsync("code", "http://localhost/callback")
            .GetAwaiter().GetResult();

        return (result is not null
            && result.AccessToken == payload.AccessToken
            && result.RefreshToken == payload.RefreshToken
            && result.ExpiresIn == payload.ExpiresIn
            && result.IdToken == payload.IdToken)
            .ToProperty();
    }

    /// <summary>
    /// Property 6: Non-Success Code Exchange Returns Null.
    /// For any non-success HTTP status code, ExchangeCodeForTokensAsync returns null.
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 20, Arbitrary = new[] { typeof(IdpServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 6: Non-success code exchange returns null")]
    public Property ExchangeCode_NonSuccessStatus_ReturnsNull(FailureStatusCode statusCode)
    {
        var (service, _) = CreateService(statusCode.Code, "error");

        var result = service.ExchangeCodeForTokensAsync("code", "http://localhost/callback")
            .GetAwaiter().GetResult();

        return (result == null).ToProperty();
    }

    /// <summary>
    /// Property 9: Userinfo Response Mapping.
    /// For any valid userinfo JSON response with sub and email present,
    /// the result maps correctly to ExternalUserInfo.
    /// **Validates: Requirement 7.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(IdpServiceArbitraries) },
        DisplayName = "Feature: phase-10b-keycloak-provider, Property 9: Userinfo response mapping")]
    public Property GetUserInfo_ValidResponse_MapsToExternalUserInfo(UserInfoPayload payload)
    {
        // Only test when both sub and email are present (service returns null otherwise)
        if (string.IsNullOrWhiteSpace(payload.Sub) || string.IsNullOrWhiteSpace(payload.Email))
        {
            return true.ToProperty();
        }

        var responseJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sub"] = payload.Sub,
            ["email"] = payload.Email,
            ["preferred_username"] = payload.PreferredUsername,
            ["name"] = payload.Name
        });

        var (service, _) = CreateService(HttpStatusCode.OK, responseJson);

        var result = service.GetUserInfoAsync("valid-access-token")
            .GetAwaiter().GetResult();

        return (result is not null
            && result.ExternalUserId == payload.Sub
            && result.Email == payload.Email
            && result.DisplayName == (payload.Name ?? payload.PreferredUsername))
            .ToProperty();
    }

    /// <summary>
    /// Userinfo returns null on 401 response.
    /// **Validates: Requirements 7.4, 7.5**
    /// </summary>
    [Fact]
    public async Task GetUserInfo_Unauthorized_ReturnsNull()
    {
        var (service, _) = CreateService(HttpStatusCode.Unauthorized, "unauthorized");

        var result = await service.GetUserInfoAsync("invalid-token");

        result.Should().BeNull();
    }

    /// <summary>
    /// ExchangeCodeForTokensAsync throws when 200 response has malformed JSON.
    /// **Validates: Requirement 5.3**
    /// </summary>
    [Fact]
    public async Task ExchangeCode_MalformedJson_Throws()
    {
        var (service, _) = CreateService(HttpStatusCode.OK, "not-json-at-all{{}");

        var act = () => service.ExchangeCodeForTokensAsync("code", "http://localhost/callback");

        await act.Should().ThrowAsync<JsonException>();
    }

    /// <summary>
    /// ExchangeCodeForTokensAsync throws when 200 response has null access_token.
    /// **Validates: Requirement 5.3**
    /// </summary>
    [Fact]
    public async Task ExchangeCode_NullAccessToken_Throws()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = (string?)null,
            expires_in = 300
        });

        var (service, _) = CreateService(HttpStatusCode.OK, responseJson);

        var act = () => service.ExchangeCodeForTokensAsync("code", "http://localhost/callback");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}

/// <summary>
/// Generated input for token exchange requests.
/// </summary>
public sealed class TokenExchangeInput
{
    public string Code { get; }
    public string RedirectUri { get; }
    public string? Realm { get; }
    public string? ClientId { get; }
    public string? CodeVerifier { get; }

    public TokenExchangeInput(string code, string redirectUri, string? realm, string? clientId, string? codeVerifier)
    {
        Code = code;
        RedirectUri = redirectUri;
        Realm = realm;
        ClientId = clientId;
        CodeVerifier = codeVerifier;
    }

    public override string ToString() =>
        $"Code={Code}, Redirect={RedirectUri}, Realm={Realm}, Client={ClientId}, Verifier={CodeVerifier != null}";
}

/// <summary>
/// Generated token response payload for mapping tests.
/// </summary>
public sealed class TokenResponsePayload
{
    public string AccessToken { get; }
    public string? RefreshToken { get; }
    public int ExpiresIn { get; }
    public string? IdToken { get; }

    public TokenResponsePayload(string accessToken, string? refreshToken, int expiresIn, string? idToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresIn = expiresIn;
        IdToken = idToken;
    }

    public override string ToString() => $"AT={AccessToken}, Expires={ExpiresIn}";
}

/// <summary>
/// Generated userinfo payload for mapping tests.
/// </summary>
public sealed class UserInfoPayload
{
    public string Sub { get; }
    public string? Email { get; }
    public string? PreferredUsername { get; }
    public string? Name { get; }

    public UserInfoPayload(string sub, string? email, string? preferredUsername, string? name)
    {
        Sub = sub;
        Email = email;
        PreferredUsername = preferredUsername;
        Name = name;
    }

    public override string ToString() => $"Sub={Sub}, Email={Email}";
}

/// <summary>
/// FsCheck Arbitrary generators for KeycloakIdentityProviderService tests.
/// </summary>
public static class IdpServiceArbitraries
{
    public static Arbitrary<TokenExchangeInput> TokenExchangeInputArb()
    {
        var codes = Gen.Elements("abc123", "code-xyz", "auth_code_42", "pkce-code-001");
        var redirects = Gen.Elements(
            "http://localhost:3000/callback",
            "https://myapp.com/auth/callback",
            "http://127.0.0.1:5000/oauth2/redirect");
        var realms = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("groundup", "custom-realm", "tenant-abc"));
        var clientIds = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("custom-client", "mobile-app", "spa-client"));
        var codeVerifiers = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", "verifier-abc-123"));

        var gen = from code in codes
                  from redirect in redirects
                  from realm in realms
                  from clientId in clientIds
                  from verifier in codeVerifiers
                  select new TokenExchangeInput(code, redirect, realm, clientId, verifier);

        return Arb.From(gen);
    }

    public static Arbitrary<TokenResponsePayload> TokenResponsePayloadArb()
    {
        var accessTokens = Gen.Elements("eyJhbGciOiJSUzI1NiJ9.token1", "eyJ.at.sig", "access-token-xyz");
        var refreshTokens = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("rt-abc", "refresh-token-123"));
        var expiresIn = Gen.Choose(60, 3600);
        var idTokens = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("id-token-abc", "eyJ.id.sig"));

        var gen = from at in accessTokens
                  from rt in refreshTokens
                  from exp in expiresIn
                  from idt in idTokens
                  select new TokenResponsePayload(at, rt, exp, idt);

        return Arb.From(gen);
    }

    public static Arbitrary<UserInfoPayload> UserInfoPayloadArb()
    {
        var subs = Gen.Elements("user-001", "f47ac10b-58cc-4372-a567-0e02b2c3d479", "auth0|12345");
        var emails = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("user@example.com", "admin@company.io", "test@test.org"));
        var usernames = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("johndoe", "admin_user", "jane.smith"));
        var names = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("John Doe", "Jane Smith", "Admin User"));

        var gen = from sub in subs
                  from email in emails
                  from username in usernames
                  from name in names
                  select new UserInfoPayload(sub, email, username, name);

        return Arb.From(gen);
    }

    public static Arbitrary<FailureStatusCode> FailureStatusCodeArb()
    {
        var gen = Gen.Elements(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout)
            .Select(code => new FailureStatusCode(code));

        return Arb.From(gen);
    }
}

/// <summary>
/// Mock HTTP handler for IdP service tests that captures request details.
/// </summary>
internal sealed class IdpMockHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string? _responseBody;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public IdpMockHandler(HttpStatusCode statusCode, string? responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var response = new HttpResponseMessage(_statusCode);
        if (_responseBody is not null)
        {
            response.Content = new StringContent(_responseBody, Encoding.UTF8, "application/json");
        }

        return response;
    }
}
