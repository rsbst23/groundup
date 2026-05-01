using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsAdminService"/> Group CRUD operations.
/// </summary>
public sealed class SettingsAdminServiceGroupTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;

    public SettingsAdminServiceGroupTests()
    {
        _fixture = new SettingsTestFixture();
    }

    private SettingsAdminService CreateService(TestSettingsDbContext context)
    {
        return new SettingsAdminService(context);
    }

    [Fact]
    public async Task GetAllGroupsAsync_ReturnsAllGroups()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        await SettingsTestFixture.SeedGroupAsync(context, "GroupA", "Group A");
        await SettingsTestFixture.SeedGroupAsync(context, "GroupB", "Group B");
        var service = CreateService(context);

        // Act
        var result = await service.GetAllGroupsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task GetAllGroupsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.GetAllGroupsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task CreateGroupAsync_CreatesAndReturnsDto()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var dto = new CreateSettingGroupDto("DatabaseConnection", "Database Connection", "DB settings", "db-icon", 0);

        // Act
        var result = await service.CreateGroupAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("DatabaseConnection", result.Data!.Key);
        Assert.Equal("Database Connection", result.Data.DisplayName);
        Assert.Equal("DB settings", result.Data.Description);
        Assert.Equal("db-icon", result.Data.Icon);
        Assert.Equal(0, result.Data.DisplayOrder);
        Assert.NotEqual(Guid.Empty, result.Data.Id);

        // Verify persisted
        var persisted = await context.Set<SettingGroup>().FirstOrDefaultAsync(g => g.Id == result.Data.Id);
        Assert.NotNull(persisted);
        Assert.Equal("DatabaseConnection", persisted.Key);
    }

    [Fact]
    public async Task UpdateGroupAsync_ExistingGroup_UpdatesAndReturnsDto()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "OldKey", "Old Name");
        var service = CreateService(context);
        var dto = new UpdateSettingGroupDto("NewKey", "New Name", "New description", "new-icon", 5);

        // Act
        var result = await service.UpdateGroupAsync(groupId, dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("NewKey", result.Data!.Key);
        Assert.Equal("New Name", result.Data.DisplayName);
        Assert.Equal("New description", result.Data.Description);
        Assert.Equal("new-icon", result.Data.Icon);
        Assert.Equal(5, result.Data.DisplayOrder);
    }

    [Fact]
    public async Task UpdateGroupAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var dto = new UpdateSettingGroupDto("Key", "Name", null, null, 0);

        // Act
        var result = await service.UpdateGroupAsync(Guid.NewGuid(), dto);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteGroupAsync_OrphansDefinitions()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "TestGroup", "Test Group");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, "TestSetting", groupId: groupId);
        var service = CreateService(context);

        // Act
        var result = await service.DeleteGroupAsync(groupId);

        // Assert
        Assert.True(result.Success);

        // Verify group is removed
        var groupExists = await context.Set<SettingGroup>().AnyAsync(g => g.Id == groupId);
        Assert.False(groupExists);

        // Verify definition still exists but GroupId is null
        var updatedDef = await context.Set<SettingDefinition>().FirstOrDefaultAsync(d => d.Id == definition.Id);
        Assert.NotNull(updatedDef);
        Assert.Null(updatedDef.GroupId);
    }

    [Fact]
    public async Task DeleteGroupAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.DeleteGroupAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteGroupAsync_NoDefinitions_Succeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "EmptyGroup", "Empty Group");
        var service = CreateService(context);

        // Act
        var result = await service.DeleteGroupAsync(groupId);

        // Assert
        Assert.True(result.Success);
        var exists = await context.Set<SettingGroup>().AnyAsync(g => g.Id == groupId);
        Assert.False(exists);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
