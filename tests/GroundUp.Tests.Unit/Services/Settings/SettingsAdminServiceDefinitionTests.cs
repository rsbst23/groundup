using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsAdminService"/> Definition CRUD operations.
/// </summary>
public sealed class SettingsAdminServiceDefinitionTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;

    public SettingsAdminServiceDefinitionTests()
    {
        _fixture = new SettingsTestFixture();
    }

    private SettingsAdminService CreateService(TestSettingsDbContext context)
    {
        return new SettingsAdminService(context);
    }

    [Fact]
    public async Task GetAllDefinitionsAsync_IncludesOptions()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            context, "AppTheme", SettingDataType.String, "light", allowedLevelIds: levelId);

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
        var result = await service.GetAllDefinitionsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("AppTheme", result.Data[0].Key);
    }

    [Fact]
    public async Task GetDefinitionByIdAsync_ExistingId_ReturnsDto()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            context, "MaxUploadSizeMB", SettingDataType.Int, "50", allowedLevelIds: levelId);

        var service = CreateService(context);

        // Act
        var result = await service.GetDefinitionByIdAsync(definition.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("MaxUploadSizeMB", result.Data!.Key);
        Assert.Equal(SettingDataType.Int, result.Data.DataType);
        Assert.Equal("50", result.Data.DefaultValue);
    }

    [Fact]
    public async Task GetDefinitionByIdAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.GetDefinitionByIdAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task CreateDefinitionAsync_PersistsOptionsAndAllowedLevels()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant", 1);
        var service = CreateService(context);

        var dto = new CreateSettingDefinitionDto(
            Key: "AppTheme",
            DataType: SettingDataType.String,
            DefaultValue: "light",
            GroupId: null,
            DisplayName: "Application Theme",
            Description: "The application theme",
            Placeholder: null,
            Category: null,
            DisplayOrder: 0,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: new List<CreateSettingOptionDto>
            {
                new("light", "Light", 0, true, null),
                new("dark", "Dark", 1, false, null),
                new("auto", "Auto", 2, false, null)
            },
            AllowedLevelIds: new List<Guid> { systemLevelId, tenantLevelId });

        // Act
        var result = await service.CreateDefinitionAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("AppTheme", result.Data!.Key);

        // Verify options persisted
        var options = await context.Set<SettingOption>()
            .Where(o => o.SettingDefinitionId == result.Data.Id)
            .ToListAsync();
        Assert.Equal(3, options.Count);
        Assert.Contains(options, o => o.Value == "light" && o.IsDefault);
        Assert.Contains(options, o => o.Value == "dark" && !o.IsDefault);
        Assert.Contains(options, o => o.Value == "auto" && !o.IsDefault);

        // Verify allowed levels persisted
        var allowedLevels = await context.Set<SettingDefinitionLevel>()
            .Where(dl => dl.SettingDefinitionId == result.Data.Id)
            .ToListAsync();
        Assert.Equal(2, allowedLevels.Count);
        Assert.Contains(allowedLevels, al => al.SettingLevelId == systemLevelId);
        Assert.Contains(allowedLevels, al => al.SettingLevelId == tenantLevelId);
    }

    [Fact]
    public async Task CreateDefinitionAsync_NoOptionsOrLevels_Succeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        var dto = new CreateSettingDefinitionDto(
            Key: "SimpleSetting",
            DataType: SettingDataType.String,
            DefaultValue: "default",
            GroupId: null,
            DisplayName: "Simple Setting",
            Description: null,
            Placeholder: null,
            Category: null,
            DisplayOrder: 0,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: null,
            AllowedLevelIds: null);

        // Act
        var result = await service.CreateDefinitionAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("SimpleSetting", result.Data!.Key);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_ReplacesOptionsAndAllowedLevels()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 0);
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant", 1);

        // Create initial definition with options and levels
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            context, "AppTheme", SettingDataType.String, "light", allowedLevelIds: systemLevelId);

        context.Set<SettingOption>().Add(new SettingOption
        {
            Id = Guid.NewGuid(),
            SettingDefinitionId = definition.Id,
            Value = "light",
            Label = "Light",
            DisplayOrder = 0,
            IsDefault = true
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var updateDto = new UpdateSettingDefinitionDto(
            Key: "AppTheme",
            DataType: SettingDataType.String,
            DefaultValue: "dark",
            GroupId: null,
            DisplayName: "Application Theme Updated",
            Description: "Updated description",
            Placeholder: null,
            Category: null,
            DisplayOrder: 1,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: new List<CreateSettingOptionDto>
            {
                new("dark", "Dark", 0, true, null),
                new("midnight", "Midnight", 1, false, null)
            },
            AllowedLevelIds: new List<Guid> { systemLevelId, tenantLevelId });

        // Act
        var result = await service.UpdateDefinitionAsync(definition.Id, updateDto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("dark", result.Data!.DefaultValue);
        Assert.Equal("Application Theme Updated", result.Data.DisplayName);

        // Verify options replaced (old "light" removed, new "dark" and "midnight" added)
        var options = await context.Set<SettingOption>()
            .Where(o => o.SettingDefinitionId == definition.Id)
            .ToListAsync();
        Assert.Equal(2, options.Count);
        Assert.Contains(options, o => o.Value == "dark" && o.IsDefault);
        Assert.Contains(options, o => o.Value == "midnight" && !o.IsDefault);
        Assert.DoesNotContain(options, o => o.Value == "light");

        // Verify allowed levels replaced
        var allowedLevels = await context.Set<SettingDefinitionLevel>()
            .Where(dl => dl.SettingDefinitionId == definition.Id)
            .ToListAsync();
        Assert.Equal(2, allowedLevels.Count);
        Assert.Contains(allowedLevels, al => al.SettingLevelId == systemLevelId);
        Assert.Contains(allowedLevels, al => al.SettingLevelId == tenantLevelId);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        var dto = new UpdateSettingDefinitionDto(
            Key: "Test",
            DataType: SettingDataType.String,
            DefaultValue: null,
            GroupId: null,
            DisplayName: "Test",
            Description: null,
            Placeholder: null,
            Category: null,
            DisplayOrder: 0,
            IsVisible: true,
            IsReadOnly: false,
            AllowMultiple: false,
            IsEncrypted: false,
            IsSecret: false,
            IsRequired: false,
            MinValue: null,
            MaxValue: null,
            MinLength: null,
            MaxLength: null,
            RegexPattern: null,
            ValidationMessage: null,
            DependsOnKey: null,
            DependsOnOperator: null,
            DependsOnValue: null,
            CustomValidatorType: null,
            Options: null,
            AllowedLevelIds: null);

        // Act
        var result = await service.UpdateDefinitionAsync(Guid.NewGuid(), dto);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesDefinition()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, "ToDelete");
        var service = CreateService(context);

        // Act
        var result = await service.DeleteDefinitionAsync(definition.Id);

        // Assert
        Assert.True(result.Success);
        var exists = await context.Set<SettingDefinition>().AnyAsync(d => d.Id == definition.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_InvalidId_ReturnsNotFound()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = CreateService(context);

        // Act
        var result = await service.DeleteDefinitionAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
