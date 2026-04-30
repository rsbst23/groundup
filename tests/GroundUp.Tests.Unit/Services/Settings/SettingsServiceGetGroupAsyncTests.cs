using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceGetGroupAsyncTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task GetGroupAsync_GroupNotFound_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry>();

        // Act
        var result = await service.GetGroupAsync("NonExistentGroup", scopeChain);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetGroupAsync_ResolvesSettingsInGroup()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "DatabaseGroup", "Database Settings");

        var def1 = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DbHost",
            dataType: SettingDataType.String,
            defaultValue: "localhost",
            groupId: groupId,
            allowedLevelIds: levelId);

        var def2 = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DbPort",
            dataType: SettingDataType.Int,
            defaultValue: "5432",
            groupId: groupId,
            allowedLevelIds: levelId);

        // Also seed a setting NOT in this group
        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "OtherSetting",
            dataType: SettingDataType.String,
            defaultValue: "other",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, def1.Id, levelId, null, "prod-db.example.com");

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetGroupAsync("DatabaseGroup", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(r => r.Definition.Key).Should().Contain("DbHost");
        result.Data!.Select(r => r.Definition.Key).Should().Contain("DbPort");
        result.Data!.Select(r => r.Definition.Key).Should().NotContain("OtherSetting");

        var hostSetting = result.Data!.First(r => r.Definition.Key == "DbHost");
        hostSetting.EffectiveValue.Should().Be("prod-db.example.com");
    }

    [Fact]
    public async Task GetGroupAsync_OrderedByDisplayOrder()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "OrderedGroup");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Third",
            defaultValue: "c",
            displayOrder: 30,
            groupId: groupId,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "First",
            defaultValue: "a",
            displayOrder: 10,
            groupId: groupId,
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Second",
            defaultValue: "b",
            displayOrder: 20,
            groupId: groupId,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetGroupAsync("OrderedGroup", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        var keys = result.Data!.Select(r => r.Definition.Key).ToList();
        keys.Should().ContainInOrder("First", "Second", "Third");
    }

    public void Dispose() => _fixture.Dispose();
}
