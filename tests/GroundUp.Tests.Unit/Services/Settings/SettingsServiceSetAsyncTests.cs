using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceSetAsyncTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task SetAsync_KeyNotFound_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("NonExistent", "value", Guid.NewGuid(), null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task SetAsync_DisallowedLevel_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var allowedLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var disallowedLevelId = await SettingsTestFixture.SeedLevelAsync(context, "User");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Setting1",
            allowedLevelIds: allowedLevelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("Setting1", "value", disallowedLevelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("not allowed");
    }

    [Fact]
    public async Task SetAsync_RequiredValueEmpty_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "RequiredSetting",
            isRequired: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("RequiredSetting", "", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("requires a value");
    }

    [Fact]
    public async Task SetAsync_BelowMinValue_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MinSetting",
            dataType: SettingDataType.Int,
            minValue: "10",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("MinSetting", "5", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("at least 10");
    }

    [Fact]
    public async Task SetAsync_AboveMaxValue_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MaxSetting",
            dataType: SettingDataType.Int,
            maxValue: "100",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("MaxSetting", "150", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("at most 100");
    }

    [Fact]
    public async Task SetAsync_BelowMinLength_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "LenSetting",
            minLength: 5,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("LenSetting", "abc", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("at least 5 characters");
    }

    [Fact]
    public async Task SetAsync_AboveMaxLength_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MaxLenSetting",
            maxLength: 5,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("MaxLenSetting", "toolongvalue", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("at most 5 characters");
    }

    [Fact]
    public async Task SetAsync_RegexMismatch_ReturnsBadRequestWithValidationMessage()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EmailSetting",
            regexPattern: @"^[\w.-]+@[\w.-]+\.\w+$",
            validationMessage: "Must be a valid email address",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("EmailSetting", "not-an-email", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("Must be a valid email address");
    }

    [Fact]
    public async Task SetAsync_ReadOnly_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ReadOnlySetting",
            isReadOnly: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("ReadOnlySetting", "new_value", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("read-only");
    }

    [Fact]
    public async Task SetAsync_CreatesNewValue_WhenNoneExists()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "NewSetting",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("NewSetting", "hello", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Value.Should().Be("hello");
        result.Data.LevelId.Should().Be(levelId);
    }

    [Fact]
    public async Task SetAsync_UpdatesExistingValue_WhenMatchExists()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "UpdateSetting",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "old_value");

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("UpdateSetting", "new_value", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Value.Should().Be("new_value");
    }

    [Fact]
    public async Task SetAsync_EncryptsValueOnWrite()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncryptedSetting",
            isEncrypted: true,
            allowedLevelIds: levelId);

        _fixture.EncryptionProvider.Encrypt("plaintext_value").Returns("encrypted_output");

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("EncryptedSetting", "plaintext_value", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().Be("encrypted_output");
        _fixture.EncryptionProvider.Received(1).Encrypt("plaintext_value");
    }

    [Fact]
    public async Task SetAsync_EncryptedWithoutProvider_ReturnsFail()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncNoProvider",
            isEncrypted: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context, encryptionProvider: null);

        // Act
        var result = await service.SetAsync("EncNoProvider", "value", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("Encryption provider required");
    }

    [Fact]
    public async Task SetAsync_EmptyValueWithMinLength_WhenNotRequired_Succeeds()
    {
        // Arrange — IsRequired=false and MinLength=5; empty value should not be rejected
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "OptionalMinLen",
            isRequired: false,
            minLength: 5,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("OptionalMinLen", "", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Value.Should().Be("");
    }

    public void Dispose() => _fixture.Dispose();
}
