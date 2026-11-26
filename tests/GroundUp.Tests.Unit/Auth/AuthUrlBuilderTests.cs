using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth;

public sealed class AuthUrlBuilderTests
{
    private readonly KeycloakOptions _keycloakOptions;
    private readonly AuthUrlBuilderService _sut;

    public AuthUrlBuilderTests()
    {
        _keycloakOptions = new KeycloakOptions
        {
            PublicBaseUrl = "https://keycloak.example.com",
            SharedRealmName = "shared-realm",
            AppClientId = "groundup-app"
        };

        var options = Options.Create(_keycloakOptions);
        _sut = new AuthUrlBuilderService(options);
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_EnterpriseTenantWithRealmName_UsesEnterpriseTenantRealm()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: "bigco-realm",
            RedirectUri: "https://bigco.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AuthorizationUrl.Should().Contain("/realms/bigco-realm/protocol/openid-connect/auth");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_NullRealmOverride_UsesSharedRealmName()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AuthorizationUrl.Should().Contain("/realms/shared-realm/protocol/openid-connect/auth");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAuthorizationUrlAsync_PublicBaseUrlNullOrWhitespace_ReturnsFailure(string? publicBaseUrl)
    {
        // Arrange
        _keycloakOptions.PublicBaseUrl = publicBaseUrl!;
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CONFIGURATION");
        result.Message.Should().Contain("PublicBaseUrl");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAuthorizationUrlAsync_AppClientIdNullOrWhitespace_ReturnsFailure(string? appClientId)
    {
        // Arrange
        _keycloakOptions.AppClientId = appClientId!;
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CONFIGURATION");
        result.Message.Should().Contain("AppClientId");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsResponseTypeCode()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AuthorizationUrl.Should().Contain("response_type=code");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsScopeOpenidEmailProfile()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        // scope is URL-encoded: "openid email profile" → "openid%20email%20profile"
        result.Data!.AuthorizationUrl.Should().Contain("scope=openid%20email%20profile");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsClientId()
    {
        // Arrange
        _keycloakOptions.AppClientId = "my-client-id";
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AuthorizationUrl.Should().Contain("client_id=my-client-id");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsNonEmptyState()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.StateToken.Should().NotBeNullOrWhiteSpace();
        result.Data!.AuthorizationUrl.Should().Contain("state=");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsNonEmptyNonce()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Nonce.Should().NotBeNullOrWhiteSpace();
        result.Data!.AuthorizationUrl.Should().Contain("nonce=");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_UrlContainsCodeChallengeAndS256Method()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AuthorizationUrl.Should().Contain("code_challenge=");
        result.Data!.AuthorizationUrl.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_RedirectUriInUrlMatchesConfiguredCallback()
    {
        // Arrange
        var callbackUrl = "https://app.example.com/auth/callback";
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: callbackUrl);

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var encodedCallback = Uri.EscapeDataString(callbackUrl);
        result.Data!.AuthorizationUrl.Should().Contain($"redirect_uri={encodedCallback}");
        result.Data!.RedirectUri.Should().Be(callbackUrl);
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ValidConfig_ResultContainsAllGeneratedValues()
    {
        // Arrange
        var callbackUrl = "https://app.example.com/auth/callback";
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: callbackUrl);

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.Data!;

        data.AuthorizationUrl.Should().NotBeNullOrWhiteSpace();
        data.StateToken.Should().NotBeNullOrWhiteSpace();
        data.Nonce.Should().NotBeNullOrWhiteSpace();
        data.CodeVerifier.Should().NotBeNullOrWhiteSpace();
        data.RedirectUri.Should().Be(callbackUrl);

        // State and Nonce should be distinct values
        data.StateToken.Should().NotBe(data.Nonce);

        // CodeVerifier should be between 43 and 128 characters per RFC 7636
        data.CodeVerifier.Length.Should().BeInRange(43, 128);
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_IsSignupTrue_UrlRequestsRegistrationWithPromptCreate()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback",
            IsSignup: true);

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert — prompt=create (Keycloak 26.1+) opens the registration form directly
        result.Success.Should().BeTrue();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["prompt"].Should().Be("create");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_IsSignupTrue_DoesNotUseKcActionRegister()
    {
        // Arrange — kc_action=register was the wrong mechanism: kc_action runs a required action
        // for an existing account, and "register" is not one, so Keycloak ignored it
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback",
            IsSignup: true);

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["kc_action"].Should().BeNull();
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_IsSignupFalse_UrlHasNoPromptParameter()
    {
        // Arrange
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback",
            IsSignup: false);

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert — login keeps Keycloak's standard login form
        result.Success.Should().BeTrue();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["prompt"].Should().BeNull();
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_MaxAgeSet_UrlContainsMaxAge()
    {
        var request = new AuthUrlRequest(null, "https://app.example.com/auth/callback", MaxAgeSeconds: 28800);

        var result = await _sut.BuildAuthorizationUrlAsync(request);

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["max_age"].Should().Be("28800");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_MaxAgeNotSet_UrlHasNoMaxAge()
    {
        var request = new AuthUrlRequest(null, "https://app.example.com/auth/callback");

        var result = await _sut.BuildAuthorizationUrlAsync(request);

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["max_age"].Should().BeNull();
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_ForceLogin_UrlContainsPromptLogin()
    {
        var request = new AuthUrlRequest(null, "https://app.example.com/auth/callback", ForceLogin: true, MaxAgeSeconds: 600);

        var result = await _sut.BuildAuthorizationUrlAsync(request);

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["prompt"].Should().Be("login");
        query["max_age"].Should().Be("600");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_SignupWithForceLogin_KeepsPromptCreate()
    {
        // A sign-up form always asks for credentials; prompt=create wins over prompt=login.
        var request = new AuthUrlRequest(null, "https://app.example.com/auth/callback", IsSignup: true, ForceLogin: true);

        var result = await _sut.BuildAuthorizationUrlAsync(request);

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["prompt"].Should().Be("create");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_IsSignupDefault_UrlHasNoPromptParameter()
    {
        // Arrange — IsSignup defaults to false
        var request = new AuthUrlRequest(
            RealmOverride: null,
            RedirectUri: "https://app.example.com/auth/callback");

        // Act
        var result = await _sut.BuildAuthorizationUrlAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(result.Data!.AuthorizationUrl).Query);
        query["prompt"].Should().BeNull();
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_LoginHint_IsAddedEscaped()
    {
        var request = new AuthUrlRequest(null, "https://app.example.com/auth/callback", LoginHint: "ana+test@example.com");

        var result = await _sut.BuildAuthorizationUrlAsync(request);

        result.Data!.AuthorizationUrl.Should().Contain("login_hint=ana%2Btest%40example.com");
    }

    [Fact]
    public async Task BuildAuthorizationUrlAsync_NoLoginHint_LeavesItOut()
    {
        var result = await _sut.BuildAuthorizationUrlAsync(new AuthUrlRequest(null, "https://app.example.com/auth/callback"));

        result.Data!.AuthorizationUrl.Should().NotContain("login_hint");
    }
}
