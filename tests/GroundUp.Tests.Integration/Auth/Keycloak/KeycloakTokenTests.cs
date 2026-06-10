using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// Integration tests for token exchange and validation against a real Keycloak container.
/// Validates: Requirements 5.1, 5.3, 6.1, 6.6, 15.2
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakTokenTests
{
    private readonly KeycloakFixture _fixture;

    public KeycloakTokenTests(KeycloakFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies token validation against real JWKS from the Keycloak container.
    /// Acquires a token via direct access grant and validates it.
    /// </summary>
    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsTrue()
    {
        // Arrange — acquire a real token using resource owner password grant
        var adminToken = await _fixture.GetAdminTokenAsync();

        // Create a test user
        var testEmail = $"test-{Guid.NewGuid():N}@example.com";
        var testPassword = "TestPass123!";
        await CreateTestUser(adminToken, testEmail, testPassword);

        // Get a token via direct access grant (already enabled on the app client)
        var accessToken = await GetUserToken(testEmail, testPassword);
        accessToken.Should().NotBeNullOrEmpty();

        // Act — validate the token using our service
        var factory = _fixture.CreateHttpClientFactory();
        var optionsMonitor = _fixture.CreateRealmOptionsMonitor();
        var jwksCache = new JwksCache(factory);
        var logger = NullLogger<KeycloakIdentityProviderService>.Instance;
        var service = new KeycloakIdentityProviderService(factory, optionsMonitor, jwksCache, logger);

        var result = await service.ValidateTokenAsync(accessToken);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an invalid/tampered token fails validation.
    /// </summary>
    [Fact]
    public async Task ValidateTokenAsync_TamperedToken_ReturnsFalse()
    {
        // Arrange
        var factory = _fixture.CreateHttpClientFactory();
        var optionsMonitor = _fixture.CreateRealmOptionsMonitor();
        var jwksCache = new JwksCache(factory);
        var logger = NullLogger<KeycloakIdentityProviderService>.Instance;
        var service = new KeycloakIdentityProviderService(factory, optionsMonitor, jwksCache, logger);

        var fakeToken = "eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwOi8vZmFrZSJ9.invalidsig";

        // Act
        var result = await service.ValidateTokenAsync(fakeToken);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies GetUserInfoAsync returns user information for a valid access token.
    /// </summary>
    [Fact]
    public async Task GetUserInfoAsync_ValidToken_ReturnsUserInfo()
    {
        // Arrange
        var adminToken = await _fixture.GetAdminTokenAsync();

        var testEmail = $"userinfo-{Guid.NewGuid():N}@example.com";
        var testPassword = "TestPass123!";
        await CreateTestUser(adminToken, testEmail, testPassword);

        var accessToken = await GetUserToken(testEmail, testPassword);

        var factory = _fixture.CreateHttpClientFactory();
        var optionsMonitor = _fixture.CreateRealmOptionsMonitor();
        var jwksCache = new JwksCache(factory);
        var logger = NullLogger<KeycloakIdentityProviderService>.Instance;
        var service = new KeycloakIdentityProviderService(factory, optionsMonitor, jwksCache, logger);

        // Act
        var userInfo = await service.GetUserInfoAsync(accessToken);

        // Assert
        userInfo.Should().NotBeNull();
        userInfo!.Email.Should().Be(testEmail);
        userInfo.ExternalUserId.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies GetUserInfoAsync returns null for an invalid token.
    /// </summary>
    [Fact]
    public async Task GetUserInfoAsync_InvalidToken_ReturnsNull()
    {
        var factory = _fixture.CreateHttpClientFactory();
        var optionsMonitor = _fixture.CreateRealmOptionsMonitor();
        var jwksCache = new JwksCache(factory);
        var logger = NullLogger<KeycloakIdentityProviderService>.Instance;
        var service = new KeycloakIdentityProviderService(factory, optionsMonitor, jwksCache, logger);

        // Act
        var result = await service.GetUserInfoAsync("invalid-token");

        // Assert
        result.Should().BeNull();
    }

    private async Task CreateTestUser(string adminToken, string email, string password)
    {
        using var client = _fixture.CreateHttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var url = $"{_fixture.BaseUrl}/admin/realms/{KeycloakFixture.TestRealmName}/users";

        // Create user with credentials and explicitly no required actions
        var userBody = new
        {
            username = email,
            email,
            emailVerified = true,
            enabled = true,
            requiredActions = Array.Empty<string>(),
            credentials = new[]
            {
                new { type = "password", value = password, temporary = false }
            }
        };

        var createResponse = await client.PostAsJsonAsync(url, userBody);
        if (!createResponse.IsSuccessStatusCode)
        {
            var body = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"CreateTestUser: user creation failed ({(int)createResponse.StatusCode}): {body}");
        }
    }

    private async Task<string> GetUserToken(string email, string password)
    {
        using var client = _fixture.CreateHttpClient();
        var tokenUrl = $"{_fixture.BaseUrl}/realms/{KeycloakFixture.TestRealmName}/protocol/openid-connect/token";

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = KeycloakFixture.AppClientId,
            ["username"] = email,
            ["password"] = password,
            ["scope"] = "openid email profile"
        });

        var response = await client.PostAsync(tokenUrl, formData);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"GetUserToken: token request failed ({(int)response.StatusCode}): {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }

}
