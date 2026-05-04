using FluentAssertions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests that deleting a definition cascades to associated records (options, levels, values).
/// Verifies EF Core cascade delete behavior configured in entity configurations.
/// </summary>
public sealed class SettingsAdminServiceCascadeTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;

    public SettingsAdminServiceCascadeTests()
    {
        _fixture = new SettingsTestFixture();
    }

    private static SettingsAdminService CreateService(TestSettingsDbContext context)
    {
        return new SettingsAdminService(context);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesAssociatedOptions()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ThemeSetting",
            dataType: SettingDataType.String,
            defaultValue: "light",
            allowedLevelIds: levelId);

        // Add options
        context.Set<SettingOption>().Add(new SettingOption
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = definition.Id,
            Value = "light",
            Label = "Light",
            DisplayOrder = 0,
            IsDefault = true
        });
        context.Set<SettingOption>().Add(new SettingOption
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = definition.Id,
            Value = "dark",
            Label = "Dark",
            DisplayOrder = 1,
            IsDefault = false
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        // Act
        var result = await service.DeleteDefinitionAsync(definition.Id);

        // Assert
        result.Success.Should().BeTrue();
        var remainingOptions = await context.Set<SettingOption>()
            .Where(o => o.SettingDefinitionId == definition.Id)
            .ToListAsync();
        remainingOptions.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesAssociatedAllowedLevels()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MultiLevelSetting",
            dataType: SettingDataType.String,
            allowedLevelIds: new[] { systemLevelId, tenantLevelId });

        // Verify allowed levels exist before delete
        var levelsBefore = await context.Set<SettingDefinitionLevel>()
            .Where(dl => dl.SettingDefinitionId == definition.Id)
            .ToListAsync();
        levelsBefore.Should().HaveCount(2);

        var service = CreateService(context);

        // Act
        var result = await service.DeleteDefinitionAsync(definition.Id);

        // Assert
        result.Success.Should().BeTrue();
        var remainingLevels = await context.Set<SettingDefinitionLevel>()
            .Where(dl => dl.SettingDefinitionId == definition.Id)
            .ToListAsync();
        remainingLevels.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesAssociatedValues()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ValueCascadeSetting",
            dataType: SettingDataType.String,
            allowedLevelIds: levelId);

        // Set a value for this definition
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "some_value");
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, Guid.NewGuid(), "scoped_value");

        // Verify values exist before delete
        var valuesBefore = await context.Set<SettingValue>()
            .Where(v => v.SettingDefinitionId == definition.Id)
            .ToListAsync();
        valuesBefore.Should().HaveCount(2);

        var service = CreateService(context);

        // Act
        var result = await service.DeleteDefinitionAsync(definition.Id);

        // Assert
        result.Success.Should().BeTrue();
        var remainingValues = await context.Set<SettingValue>()
            .Where(v => v.SettingDefinitionId == definition.Id)
            .ToListAsync();
        remainingValues.Should().BeEmpty();
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
