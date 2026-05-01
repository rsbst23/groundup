using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsAdminService"/> Level CRUD operations.
/// </summary>
public sealed class SettingsAdminServiceLevelTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;

    public SettingsAdminServiceLevelTests()
    {
        _fixture = new SettingsTestFixture();
    }

    private SettingsAdminService CreateService(TestSettingsDbContext context)
    {
        return new SettingsAdminService(context);
    }

    [Fact]
    public async Task GetAllLevelsAsync_ReturnsAllLevels()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        await SettingsTestFixture.SeedLevelAsync(context, "Tenant", 1);
        var service = CreateService(context);

        // Act
        var result = await service.GetAllLevelsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("System", result.Data[0].Name);
        Assert.Equal("Tenant", result.Data[1].Name);
    }

    [Fact]
    public async Task GetAllLevelsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.GetAllLevelsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task CreateLevelAsync_CreatesAndReturnsDto()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var dto = new CreateSettingLevelDto("System", "Root level", null, 0);

        // Act
        var result = await service.CreateLevelAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("System", result.Data!.Name);
        Assert.Equal("Root level", result.Data.Description);
        Assert.Null(result.Data.ParentId);
        Assert.Equal(0, result.Data.DisplayOrder);
        Assert.NotEqual(Guid.Empty, result.Data.Id);

        // Verify persisted
        var persisted = await context.Set<SettingLevel>().FirstOrDefaultAsync(l => l.Id == result.Data.Id);
        Assert.NotNull(persisted);
        Assert.Equal("System", persisted.Name);
    }

    [Fact]
    public async Task CreateLevelAsync_WithParent_SetsParentId()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var parentId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var service = CreateService(context);
        var dto = new CreateSettingLevelDto("Tenant", "Tenant level", parentId, 1);

        // Act
        var result = await service.CreateLevelAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(parentId, result.Data!.ParentId);
    }

    [Fact]
    public async Task UpdateLevelAsync_ExistingLevel_UpdatesAndReturnsDto()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var service = CreateService(context);
        var dto = new UpdateSettingLevelDto("Global", "Updated description", null, 5);

        // Act
        var result = await service.UpdateLevelAsync(levelId, dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Global", result.Data!.Name);
        Assert.Equal("Updated description", result.Data.Description);
        Assert.Equal(5, result.Data.DisplayOrder);
    }

    [Fact]
    public async Task UpdateLevelAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);
        var dto = new UpdateSettingLevelDto("Global", null, null, 0);

        // Act
        var result = await service.UpdateLevelAsync(Guid.NewGuid(), dto);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteLevelAsync_WithChildLevels_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var parentId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);

        // Create child level
        var child = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "Tenant",
            ParentId = parentId,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(child);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        // Act
        var result = await service.DeleteLevelAsync(parentId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("child levels", result.Message);
    }

    [Fact]
    public async Task DeleteLevelAsync_WithReferencingValues_ReturnsBadRequest()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, "TestSetting", allowedLevelIds: levelId);
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "test");

        var service = CreateService(context);

        // Act
        var result = await service.DeleteLevelAsync(levelId);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("setting values", result.Message);
    }

    [Fact]
    public async Task DeleteLevelAsync_NoReferences_Succeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var service = CreateService(context);

        // Act
        var result = await service.DeleteLevelAsync(levelId);

        // Assert
        Assert.True(result.Success);

        // Verify removed
        var exists = await context.Set<SettingLevel>().AnyAsync(l => l.Id == levelId);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteLevelAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.DeleteLevelAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
