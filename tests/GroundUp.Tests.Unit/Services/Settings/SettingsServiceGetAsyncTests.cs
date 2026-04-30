using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceGetAsyncTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>();

        // Act
        var result = await service.GetAsync<string>("NonExistentKey", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAsync_EmptyScopeChain_ReturnsDefaultValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MaxUpload",
            dataType: SettingDataType.Int,
            defaultValue: "50",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>();

        // Act
        var result = await service.GetAsync<int>("MaxUpload", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(50);
    }

    [Fact]
    public async Task GetAsync_FirstMatchInScopeChainWins()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "PageSize",
            dataType: SettingDataType.Int,
            defaultValue: "10",
            allowedLevelIds: new[] { tenantLevelId, systemLevelId });

        var tenantScopeId = Guid.NewGuid();
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, tenantLevelId, tenantScopeId, "25");
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, systemLevelId, null, "100");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>
        {
            new(tenantLevelId, tenantScopeId),
            new(systemLevelId, null)
        };

        // Act
        var result = await service.GetAsync<int>("PageSize", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(25);
    }

    [Fact]
    public async Task GetAsync_SkipsValuesAtDisallowedLevels()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var allowedLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var disallowedLevelId = await SettingsTestFixture.SeedLevelAsync(context, "User");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Theme",
            dataType: SettingDataType.String,
            defaultValue: "light",
            allowedLevelIds: allowedLevelId); // Only system level allowed

        // Seed a value at the disallowed level
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, disallowedLevelId, Guid.NewGuid(), "dark");
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, allowedLevelId, null, "blue");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>
        {
            new(disallowedLevelId, Guid.NewGuid()),
            new(allowedLevelId, null)
        };

        // Act
        var result = await service.GetAsync<string>("Theme", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("blue");
    }

    [Fact]
    public async Task GetAsync_FallsBackToDefaultWhenNoMatch()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Timeout",
            dataType: SettingDataType.Int,
            defaultValue: "30",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>
        {
            new(levelId, Guid.NewGuid()) // No value stored for this scope
        };

        // Act
        var result = await service.GetAsync<int>("Timeout", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(30);
    }

    [Fact]
    public async Task GetAsync_EncryptedSetting_DecryptsOnRead()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ApiKey",
            dataType: SettingDataType.String,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "encrypted_value");

        _fixture.EncryptionProvider.Decrypt("encrypted_value").Returns("decrypted_secret");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("ApiKey", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("decrypted_secret");
        _fixture.EncryptionProvider.Received(1).Decrypt("encrypted_value");
    }

    [Fact]
    public async Task GetAsync_EncryptedSettingWithoutProvider_ReturnsFail()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Secret",
            dataType: SettingDataType.String,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "encrypted_data");

        var service = _fixture.CreateService(context, encryptionProvider: null);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("Secret", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("Encryption provider required");
    }

    [Fact]
    public async Task GetAsync_SecretSettingViaGetAsync_ReturnsRealValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DbPassword",
            dataType: SettingDataType.String,
            isSecret: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "my_secret_password");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("DbPassword", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("my_secret_password");
    }

    [Fact]
    public async Task GetAsync_NullValue_ReturnsDefault()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "OptionalSetting",
            dataType: SettingDataType.String,
            defaultValue: null,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, Guid.NewGuid()) };

        // Act
        var result = await service.GetAsync<string>("OptionalSetting", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    public void Dispose() => _fixture.Dispose();
}
