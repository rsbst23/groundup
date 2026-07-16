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
}
