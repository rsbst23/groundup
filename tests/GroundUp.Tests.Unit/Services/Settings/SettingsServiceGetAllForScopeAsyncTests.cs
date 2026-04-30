using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceGetAllForScopeAsyncTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task GetAllForScopeAsync_ResolvesAllDefinitions()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Setting1",
            dataType: SettingDataType.String,
            defaultValue: "default1",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Setting2",
            dataType: SettingDataType.Int,
            defaultValue: "42",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(r => r.Definition.Key).Should().Contain("Setting1");
        result.Data!.Select(r => r.Definition.Key).Should().Contain("Setting2");
    }

    [Fact]
    public async Task GetAllForScopeAsync_IsInheritedFalse_WhenValueFromFirstScopeEntry()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DirectSetting",
            dataType: SettingDataType.String,
            defaultValue: "default",
            allowedLevelIds: new[] { tenantLevelId, systemLevelId });

        var tenantScopeId = Guid.NewGuid();
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, tenantLevelId, tenantScopeId, "tenant_value");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>
        {
            new(tenantLevelId, tenantScopeId),
            new(systemLevelId, null)
        };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "DirectSetting");
        resolved.IsInherited.Should().BeFalse();
        resolved.EffectiveValue.Should().Be("tenant_value");
    }

    [Fact]
    public async Task GetAllForScopeAsync_IsInheritedTrue_WhenValueFromHigherLevel()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "InheritedSetting",
            dataType: SettingDataType.String,
            defaultValue: "default",
            allowedLevelIds: new[] { tenantLevelId, systemLevelId });

        // Value only at system level (second in scope chain)
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, systemLevelId, null, "system_value");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>
        {
            new(tenantLevelId, Guid.NewGuid()),
            new(systemLevelId, null)
        };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "InheritedSetting");
        resolved.IsInherited.Should().BeTrue();
        resolved.EffectiveValue.Should().Be("system_value");
    }

    [Fact]
    public async Task GetAllForScopeAsync_MasksSecretValues()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SecretSetting",
            dataType: SettingDataType.String,
            isSecret: true,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "super_secret");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "SecretSetting");
        resolved.EffectiveValue.Should().Be("••••••••");
    }

    public void Dispose() => _fixture.Dispose();
}
