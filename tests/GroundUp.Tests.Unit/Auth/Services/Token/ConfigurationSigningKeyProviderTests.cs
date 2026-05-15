using System.Text;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Token;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Services.Token;

public sealed class ConfigurationSigningKeyProviderTests
{
    private const string ValidKey = "ThisIsAValidSigningKeyThatIs32Bytes!"; // 36 bytes UTF-8

    [Fact]
    public void Constructor_EmptySigningKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = "" });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new ConfigurationSigningKeyProvider(options));
        Assert.Contains("JwtSigningKey is not configured", ex.Message);
    }

    [Fact]
    public void Constructor_WhitespaceSigningKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = "   " });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new ConfigurationSigningKeyProvider(options));
        Assert.Contains("JwtSigningKey is not configured", ex.Message);
    }

    [Fact]
    public void Constructor_KeyShorterThan32Bytes_ThrowsInvalidOperationException()
    {
        // Arrange — 31 ASCII characters = 31 bytes
        var shortKey = new string('A', 31);
        var options = Options.Create(new AuthOptions { JwtSigningKey = shortKey });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new ConfigurationSigningKeyProvider(options));
        Assert.Contains("at least 32 bytes", ex.Message);
    }

    [Fact]
    public void Constructor_KeyExactly32Bytes_Succeeds()
    {
        // Arrange — 32 ASCII characters = 32 bytes
        var exactKey = new string('A', 32);
        var options = Options.Create(new AuthOptions { JwtSigningKey = exactKey });

        // Act
        var provider = new ConfigurationSigningKeyProvider(options);

        // Assert — no exception thrown
        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_ValidKey_Succeeds()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });

        // Act
        var provider = new ConfigurationSigningKeyProvider(options);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task GetSigningKeyAsync_AnyTenantId_ReturnsDefaultKey()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });
        var provider = new ConfigurationSigningKeyProvider(options);
        var tenantId = Guid.NewGuid();

        // Act
        var result = await provider.GetSigningKeyAsync(tenantId);

        // Assert
        Assert.Equal(ConfigurationSigningKeyProvider.DefaultKeyId, result.KeyId);
        Assert.Equal("HS256", result.Algorithm);
        Assert.Equal(Encoding.UTF8.GetBytes(ValidKey), result.KeyMaterial);
    }

    [Fact]
    public async Task GetSigningKeyAsync_DifferentTenants_ReturnsSameKey()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });
        var provider = new ConfigurationSigningKeyProvider(options);

        // Act
        var key1 = await provider.GetSigningKeyAsync(Guid.NewGuid());
        var key2 = await provider.GetSigningKeyAsync(Guid.NewGuid());

        // Assert — single-key mode returns the same key for all tenants
        Assert.Equal(key1.KeyId, key2.KeyId);
        Assert.Equal(key1.KeyMaterial, key2.KeyMaterial);
    }

    [Fact]
    public async Task GetValidationKeyAsync_DefaultKid_ReturnsKey()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });
        var provider = new ConfigurationSigningKeyProvider(options);

        // Act
        var result = await provider.GetValidationKeyAsync(ConfigurationSigningKeyProvider.DefaultKeyId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ConfigurationSigningKeyProvider.DefaultKeyId, result.KeyId);
        Assert.Equal(Encoding.UTF8.GetBytes(ValidKey), result.KeyMaterial);
    }

    [Fact]
    public async Task GetValidationKeyAsync_UnknownKid_ReturnsNull()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });
        var provider = new ConfigurationSigningKeyProvider(options);

        // Act
        var result = await provider.GetValidationKeyAsync("unknown-kid");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSigningKeyAsync_And_GetValidationKeyAsync_RoundTrip()
    {
        // Arrange
        var options = Options.Create(new AuthOptions { JwtSigningKey = ValidKey });
        var provider = new ConfigurationSigningKeyProvider(options);
        var tenantId = Guid.NewGuid();

        // Act — get signing key, then resolve it by kid
        var signingKey = await provider.GetSigningKeyAsync(tenantId);
        var validationKey = await provider.GetValidationKeyAsync(signingKey.KeyId);

        // Assert — same key material
        Assert.NotNull(validationKey);
        Assert.Equal(signingKey.KeyMaterial, validationKey.KeyMaterial);
        Assert.Equal(signingKey.Algorithm, validationKey.Algorithm);
    }
}
