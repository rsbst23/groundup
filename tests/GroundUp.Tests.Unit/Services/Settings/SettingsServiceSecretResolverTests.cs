using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for ISecretResolver integration in SettingsService.
/// Tests: resolve on read, verbatim without resolver, null resolution failure, single-pass behavior.
///
/// Requirements: 4.1–4.8
/// </summary>
public sealed class SettingsServiceSecretResolverTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    #region Resolve on Read (Req 4.2)

    [Fact]
    public async Task GetAsync_SecretRefValue_WithResolver_ResolvesOnRead()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "keycloak.client-secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null,
            "secretref://azure-kv/keycloak-secret");

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync("secretref://azure-kv/keycloak-secret", Arg.Any<CancellationToken>())
            .Returns("actual-keycloak-secret-value");

        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("keycloak.client-secret", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("actual-keycloak-secret-value");
        await secretResolver.Received(1).ResolveAsync("secretref://azure-kv/keycloak-secret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_SecretRefValue_ResolverCalledWithFullPrefix()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "aws.secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        var fullRef = "secretref://aws-sm/my-app/db-password";
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, fullRef);

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(fullRef, Arg.Any<CancellationToken>())
            .Returns("db-password-123");

        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("aws.secret", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("db-password-123");
    }

    #endregion

    #region Verbatim Without Resolver (Req 4.3)

    [Fact]
    public async Task GetAsync_SecretRefValue_NoResolverRegistered_ReturnsVerbatim()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "external.secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        var secretRefLiteral = "secretref://vault/my-secret";
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, secretRefLiteral);

        // No secret resolver registered (null)
        var service = CreateServiceWithResolver(context, secretResolver: null);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("external.secret", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(secretRefLiteral);
    }

    [Fact]
    public async Task GetAsync_NonSecretRefValue_NoResolverRegistered_ReturnsNormally()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "plain.setting",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "plain-value");

        var service = CreateServiceWithResolver(context, secretResolver: null);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("plain.setting", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("plain-value");
    }

    #endregion

    #region Null Resolution Failure (Req 4.4)

    [Fact]
    public async Task GetAsync_SecretRefValue_ResolverReturnsNull_ReturnsFailure()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "missing.secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null,
            "secretref://vault/nonexistent");

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync("secretref://vault/nonexistent", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("missing.secret", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Secret reference could not be resolved");
    }

    [Fact]
    public async Task GetAsync_SecretRefValue_ResolverReturnsNull_IncludesSettingKeyInError()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "app.api-key",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null,
            "secretref://hsm/api-key");

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("app.api-key", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("app.api-key");
    }

    #endregion

    #region Single-Pass Behavior (Req 4.8)

    [Fact]
    public async Task GetAsync_ResolverReturnsSecretRef_NotReResolved()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "chained.secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        var outerRef = "secretref://vault/outer";
        var innerRef = "secretref://vault/inner"; // Resolver returns another secretref://
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, outerRef);

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(outerRef, Arg.Any<CancellationToken>())
            .Returns(innerRef);

        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("chained.secret", scopeChain);

        // Assert: the inner secretref:// is returned verbatim, NOT re-resolved
        result.Success.Should().BeTrue();
        result.Data.Should().Be(innerRef);

        // Assert: resolver called exactly once (for the outer ref only)
        await secretResolver.Received(1).ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_NonSecretRefValue_ResolverNotCalled()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "normal.setting",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "just-a-value");

        var secretResolver = Substitute.For<ISecretResolver>();
        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("normal.setting", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("just-a-value");
        await secretResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Write Path — Literal Persistence (Req 4.6)

    [Fact]
    public async Task SetAsync_SecretRefValue_PersistsLiteral_ResolverNotCalled()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "write.secret",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        var secretRefValue = "secretref://vault/write-test";
        var secretResolver = Substitute.For<ISecretResolver>();
        var service = CreateServiceWithResolver(context, secretResolver);

        // Act
        var result = await service.SetAsync("write.secret", secretRefValue, levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        await secretResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verify the literal is stored in the database
        var storedValue = context.Set<SettingValue>()
            .FirstOrDefault(v => v.SettingDefinitionId == definition.Id);
        storedValue.Should().NotBeNull();
        storedValue!.Value.Should().Be(secretRefValue);
    }

    [Fact]
    public async Task SetAsync_SecretRefValue_DoesNotResolveBeforeStoring()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "persist.ref",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        var secretRefValue = "secretref://aws-sm/production/api-key";
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("should-never-be-called");

        var service = CreateServiceWithResolver(context, secretResolver);

        // Act
        var result = await service.SetAsync("persist.ref", secretRefValue, levelId, null);

        // Assert: write succeeded and resolver was never invoked
        result.Success.Should().BeTrue();
        await secretResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Encrypted + SecretRef Combo (Req 4.7)

    [Fact]
    public async Task GetAsync_EncryptedSecretRef_DecryptsThenResolves()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "encrypted.secretref",
            dataType: SettingDataType.String,
            isEncrypted: true,
            allowedLevelIds: levelId);

        // The stored value is encrypted ciphertext
        var encryptedValue = "aes-gcm-v1:encrypted-secretref";
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, encryptedValue);

        // After decryption, the value is a secretref://
        var decryptedSecretRef = "secretref://vault/encrypted-secret";
        _fixture.EncryptionProvider.Decrypt(encryptedValue).Returns(decryptedSecretRef);

        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(decryptedSecretRef, Arg.Any<CancellationToken>())
            .Returns("final-resolved-value");

        var service = CreateServiceWithResolverAndEncryption(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("encrypted.secretref", scopeChain);

        // Assert: decrypt first, then resolve
        result.Success.Should().BeTrue();
        result.Data.Should().Be("final-resolved-value");
        _fixture.EncryptionProvider.Received(1).Decrypt(encryptedValue);
        await secretResolver.Received(1).ResolveAsync(decryptedSecretRef, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Null/Empty/Whitespace Values — No Resolution Attempted

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_NullOrWhitespaceValue_ResolverNotCalled(string? storedValue)
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "nullable.setting",
            dataType: SettingDataType.String,
            defaultValue: "fallback",
            allowedLevelIds: levelId);

        if (storedValue is not null)
        {
            await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, storedValue);
        }

        var secretResolver = Substitute.For<ISecretResolver>();
        var service = CreateServiceWithResolver(context, secretResolver);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAsync<string>("nullable.setting", scopeChain);

        // Assert: resolver never called for null/empty/whitespace
        result.Success.Should().BeTrue();
        await secretResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helpers

    private SettingsService CreateServiceWithResolver(
        TestSettingsDbContext context,
        ISecretResolver? secretResolver)
    {
        return new SettingsService(
            context,
            _fixture.EventBus,
            _fixture.ScopeChainProvider,
            _fixture.MemoryCache,
            _fixture.CacheOptions,
            _fixture.CacheKeyTracker,
            encryptionProvider: null,
            secretResolver: secretResolver);
    }

    private SettingsService CreateServiceWithResolverAndEncryption(
        TestSettingsDbContext context,
        ISecretResolver? secretResolver)
    {
        return new SettingsService(
            context,
            _fixture.EventBus,
            _fixture.ScopeChainProvider,
            _fixture.MemoryCache,
            _fixture.CacheOptions,
            _fixture.CacheKeyTracker,
            encryptionProvider: _fixture.EncryptionProvider,
            secretResolver: secretResolver);
    }

    #endregion

    public void Dispose() => _fixture.Dispose();
}
