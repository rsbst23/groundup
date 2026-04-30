using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Events;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceEventTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task SetAsync_PublishesSettingChangedEvent_OnCreate()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "EventSetting",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(context);

        // Act
        await service.SetAsync("EventSetting", "new_value", levelId, null);

        // Assert
        await _fixture.EventBus.Received(1).PublishAsync(
            Arg.Is<SettingChangedEvent>(e =>
                e.SettingKey == "EventSetting" &&
                e.LevelId == levelId &&
                e.ScopeId == null &&
                e.OldValue == null &&
                e.NewValue == "new_value"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_PublishesSettingChangedEvent_OnUpdate_WithOldValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "UpdateEvent",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "old_val");

        var service = _fixture.CreateService(context);

        // Act
        await service.SetAsync("UpdateEvent", "new_val", levelId, null);

        // Assert
        await _fixture.EventBus.Received(1).PublishAsync(
            Arg.Is<SettingChangedEvent>(e =>
                e.SettingKey == "UpdateEvent" &&
                e.OldValue == "old_val" &&
                e.NewValue == "new_val"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteValueAsync_PublishesSettingChangedEvent_WithNullNewValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DeleteEvent",
            allowedLevelIds: levelId);

        var valueId = await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "to_delete");

        var service = _fixture.CreateService(context);

        // Act
        await service.DeleteValueAsync(valueId);

        // Assert
        await _fixture.EventBus.Received(1).PublishAsync(
            Arg.Is<SettingChangedEvent>(e =>
                e.SettingKey == "DeleteEvent" &&
                e.OldValue == "to_delete" &&
                e.NewValue == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_EventPublishingFailure_DoesNotFailOperation()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "FailEvent",
            allowedLevelIds: levelId);

        _fixture.EventBus
            .PublishAsync(Arg.Any<SettingChangedEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Event bus failure"));

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("FailEvent", "value", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    public void Dispose() => _fixture.Dispose();
}
