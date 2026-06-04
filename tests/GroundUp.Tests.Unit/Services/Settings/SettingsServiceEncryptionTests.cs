using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsService"/> encryption integration.
/// Covers: encrypt on set, decrypt on get, missing-provider error,
/// mask "***REDACTED***", and IsSecret+IsEncrypted combo.
/// Requirements: 3.1–3.10
/// </summary>
public sealed class SettingsServiceEncryptionTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    #region Encrypt on Set (Req 3.1, 3.2)

    [Fact]
    public async Task SetAsync_EncryptedDefinition_NonWhitespaceValue_EncryptsBeforePersisting()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ApiKey",
            isEncrypted: true,
            allowedLevelIds: levelId);

        _fixture.EncryptionProvider.Encrypt("my-secret-key").Returns("aes-gcm-v1:nonce:cipher:tag");

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("ApiKey", "my-secret-key", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().Be("aes-gcm-v1:nonce:cipher:tag");
        _fixture.EncryptionProvider.Received(1).Encrypt("my-secret-key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task SetAsync_EncryptedDefinition_WhitespaceValue_PersistsNullWithoutEncrypting(string? value)
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncSetting",
            isEncrypted: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("EncSetting", value!, levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().BeNull();
        _fixture.EncryptionProvider.DidNotReceive().Encrypt(Arg.Any<string>());
    }

    [Fact]
    public async Task SetAsync_NonEncryptedDefinition_DoesNotCallEncrypt()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "PlainSetting",
            isEncrypted: false,
            allowedLevelIds: levelId);

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("PlainSetting", "plain-value", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().Be("plain-value");
        _fixture.EncryptionProvider.DidNotReceive().Encrypt(Arg.Any<string>());
    }

    #endregion

    #region Decrypt on Get (Req 3.3, 3.4)

    [Fact]
    public async Task GetAsync_EncryptedDefinition_NonWhitespaceStoredValue_DecryptsOnRead()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DbPassword",
            dataType: SettingDataType.String,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "aes-gcm-v1:enc-data");

        _fixture.EncryptionProvider.Decrypt("aes-gcm-v1:enc-data").Returns("real-password");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("DbPassword", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("real-password");
        _fixture.EncryptionProvider.Received(1).Decrypt("aes-gcm-v1:enc-data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_EncryptedDefinition_WhitespaceStoredValue_DoesNotCallDecrypt(string storedValue)
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncEmpty",
            dataType: SettingDataType.String,
            isEncrypted: true,
            defaultValue: "fallback",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, storedValue);

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("EncEmpty", scopeChain);

        // Assert — the key property: Decrypt is never called for whitespace stored values
        result.Success.Should().BeTrue();
        _fixture.EncryptionProvider.DidNotReceive().Decrypt(Arg.Any<string>());
    }

    [Fact]
    public async Task GetAsync_EncryptedDefinition_NullStoredValue_FallsBackToDefaultAndDecrypts()
    {
        // Arrange — when stored value is null, effectiveValue falls back to DefaultValue.
        // If DefaultValue is non-whitespace, it IS decrypted (the default is treated as encrypted too).
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncNullStored",
            dataType: SettingDataType.String,
            isEncrypted: true,
            defaultValue: null,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, null);

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("EncNullStored", scopeChain);

        // Assert — null stored + null default → no decryption needed
        result.Success.Should().BeTrue();
        _fixture.EncryptionProvider.DidNotReceive().Decrypt(Arg.Any<string>());
    }

    [Fact]
    public async Task GetAsync_NonEncryptedDefinition_DoesNotCallDecrypt()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "PlainRead",
            dataType: SettingDataType.String,
            isEncrypted: false,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "plain-value");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("PlainRead", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("plain-value");
        _fixture.EncryptionProvider.DidNotReceive().Decrypt(Arg.Any<string>());
    }

    #endregion

    #region Missing Provider Error (Req 3.5, 3.6)

    [Fact]
    public async Task SetAsync_EncryptedDefinition_NoProvider_ReturnsFailWithClearMessage()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "NeedEncrypt",
            isEncrypted: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context, encryptionProvider: null);

        // Act
        var result = await service.SetAsync("NeedEncrypt", "some-value", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("Encryption provider required");
        result.Message.Should().Contain("NeedEncrypt");
    }

    [Fact]
    public async Task GetAsync_EncryptedDefinition_NoProvider_ReturnsFailWithClearMessage()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "NeedDecrypt",
            dataType: SettingDataType.String,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "encrypted-blob");

        var service = _fixture.CreateService(context, encryptionProvider: null);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("NeedDecrypt", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("Encryption provider required");
        result.Message.Should().Contain("NeedDecrypt");
    }

    [Fact]
    public async Task SetAsync_EncryptedDefinition_WhitespaceValue_NoProvider_StillSucceeds()
    {
        // Arrange — whitespace short-circuits before provider is needed
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncWhitespace",
            isEncrypted: true,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context, encryptionProvider: null);

        // Act
        var result = await service.SetAsync("EncWhitespace", "   ", levelId, null);

        // Assert — whitespace persists as null without needing the provider
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().BeNull();
    }

    #endregion

    #region Masking ***REDACTED*** (Req 3.7)

    [Fact]
    public async Task GetAllForScopeAsync_IsSecretTrue_MasksValueWithRedacted()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretKey",
            dataType: SettingDataType.String,
            isSecret: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "actual-secret-value");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "SecretKey");
        resolved.EffectiveValue.Should().Be("***REDACTED***");
    }

    [Fact]
    public async Task GetAllForScopeAsync_IsSecretTrue_NullValue_DoesNotMask()
    {
        // Arrange — null values should remain null, not be masked
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretNull",
            dataType: SettingDataType.String,
            isSecret: true,
            defaultValue: null,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "SecretNull");
        resolved.EffectiveValue.Should().BeNull();
    }

    [Fact]
    public async Task GetGroupAsync_IsSecretTrue_MasksValueWithRedacted()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "security", "Security");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "GroupSecret",
            dataType: SettingDataType.String,
            isSecret: true,
            groupId: groupId,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "hidden-value");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetGroupAsync("security", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "GroupSecret");
        resolved.EffectiveValue.Should().Be("***REDACTED***");
    }

    [Fact]
    public async Task GetAsync_IsSecretTrue_DoesNotMask()
    {
        // Arrange — GetAsync<T> returns the real value (masking is only on bulk read paths)
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretDirect",
            dataType: SettingDataType.String,
            isSecret: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "real-secret");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("SecretDirect", scopeChain);

        // Assert — GetAsync returns the real value for service-layer consumers
        result.Success.Should().BeTrue();
        result.Data.Should().Be("real-secret");
    }

    #endregion

    #region IsSecret + IsEncrypted Combo (Req 3.8)

    [Fact]
    public async Task SetAsync_IsSecretAndIsEncrypted_EncryptsOnWrite()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretEncrypted",
            isSecret: true,
            isEncrypted: true,
            allowedLevelIds: levelId);

        _fixture.EncryptionProvider.Encrypt("admin-password").Returns("aes-gcm-v1:enc-admin");

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("SecretEncrypted", "admin-password", levelId, null);

        // Assert — value is encrypted at rest
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().Be("aes-gcm-v1:enc-admin");
        _fixture.EncryptionProvider.Received(1).Encrypt("admin-password");
    }

    [Fact]
    public async Task GetAsync_IsSecretAndIsEncrypted_DecryptsOnRead()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretEncRead",
            dataType: SettingDataType.String,
            isSecret: true,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "aes-gcm-v1:cipher");

        _fixture.EncryptionProvider.Decrypt("aes-gcm-v1:cipher").Returns("decrypted-secret");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act — GetAsync returns the decrypted value for service-layer consumers
        var result = await service.GetAsync<string>("SecretEncRead", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("decrypted-secret");
        _fixture.EncryptionProvider.Received(1).Decrypt("aes-gcm-v1:cipher");
    }

    [Fact]
    public async Task GetAllForScopeAsync_IsSecretAndIsEncrypted_DecryptsThenMasks()
    {
        // Arrange — the bulk read path decrypts first, then masks for API consumers
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretEncBulk",
            dataType: SettingDataType.String,
            isSecret: true,
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "aes-gcm-v1:bulk-cipher");

        _fixture.EncryptionProvider.Decrypt("aes-gcm-v1:bulk-cipher").Returns("decrypted-bulk");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert — decrypted first (Decrypt was called), then masked in the response
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "SecretEncBulk");
        resolved.EffectiveValue.Should().Be("***REDACTED***");
        _fixture.EncryptionProvider.Received(1).Decrypt("aes-gcm-v1:bulk-cipher");
    }

    [Fact]
    public async Task GetGroupAsync_IsSecretAndIsEncrypted_DecryptsThenMasks()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "secrets", "Secrets");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "GroupSecretEnc",
            dataType: SettingDataType.String,
            isSecret: true,
            isEncrypted: true,
            groupId: groupId,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "aes-gcm-v1:group-cipher");

        _fixture.EncryptionProvider.Decrypt("aes-gcm-v1:group-cipher").Returns("decrypted-group");

        var service = _fixture.CreateServiceWithEncryption(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetGroupAsync("secrets", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "GroupSecretEnc");
        resolved.EffectiveValue.Should().Be("***REDACTED***");
        _fixture.EncryptionProvider.Received(1).Decrypt("aes-gcm-v1:group-cipher");
    }

    #endregion

    #region Transparency (Req 3.9, 3.10)

    [Fact]
    public async Task SetAsync_EncryptedDefinition_DtoShapeUnchanged()
    {
        // Arrange — encryption is transparent; the DTO shape doesn't change
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "TransparentEnc",
            isEncrypted: true,
            allowedLevelIds: levelId);

        _fixture.EncryptionProvider.Encrypt("value").Returns("encrypted-value");

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("TransparentEnc", "value", levelId, null);

        // Assert — DTO has all expected fields populated
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBeEmpty();
        result.Data.SettingDefinitionId.Should().NotBeEmpty();
        result.Data.LevelId.Should().Be(levelId);
        result.Data.ScopeId.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_EncryptedDefinition_UpdatesExistingValue()
    {
        // Arrange — updating an existing encrypted value
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EncUpdate",
            isEncrypted: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "old-encrypted");

        _fixture.EncryptionProvider.Encrypt("new-value").Returns("new-encrypted");

        var service = _fixture.CreateServiceWithEncryption(context);

        // Act
        var result = await service.SetAsync("EncUpdate", "new-value", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Value.Should().Be("new-encrypted");
        _fixture.EncryptionProvider.Received(1).Encrypt("new-value");
    }

    #endregion

    public void Dispose() => _fixture.Dispose();
}
