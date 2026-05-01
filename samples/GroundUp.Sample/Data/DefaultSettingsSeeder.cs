using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Sample.Data;

/// <summary>
/// Seeds example setting levels, groups, and definitions on application startup.
/// Uses check-before-insert logic for idempotency — running multiple times
/// produces the same result as running once.
/// </summary>
public sealed class DefaultSettingsSeeder : IDataSeeder
{
    private readonly SampleDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultSettingsSeeder"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context.</param>
    public DefaultSettingsSeeder(SampleDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var systemLevel = await SeedLevelAsync("System", null, 0, cancellationToken);
        var tenantLevel = await SeedLevelAsync("Tenant", systemLevel.Id, 1, cancellationToken);

        var dbGroup = await SeedGroupAsync(
            "DatabaseConnection",
            "Database Connection",
            "Database connection settings",
            null,
            0,
            cancellationToken);

        var bothLevelIds = new[] { systemLevel.Id, tenantLevel.Id };

        // MaxUploadSizeMB
        await SeedDefinitionAsync(
            key: "MaxUploadSizeMB",
            dataType: SettingDataType.Int,
            defaultValue: "50",
            groupId: null,
            displayName: "Max Upload Size (MB)",
            description: "Maximum file upload size in megabytes",
            displayOrder: 0,
            allowedLevelIds: bothLevelIds,
            options: null,
            cancellationToken: cancellationToken);

        // AppTheme
        await SeedDefinitionAsync(
            key: "AppTheme",
            dataType: SettingDataType.String,
            defaultValue: "light",
            groupId: null,
            displayName: "Application Theme",
            description: "The application color theme",
            displayOrder: 1,
            allowedLevelIds: bothLevelIds,
            options: new[]
            {
                ("light", "Light", 0, true),
                ("dark", "Dark", 1, false),
                ("auto", "Auto", 2, false)
            },
            cancellationToken: cancellationToken);

        // DatabaseConnection.Host
        await SeedDefinitionAsync(
            key: "DatabaseConnection.Host",
            dataType: SettingDataType.String,
            defaultValue: "localhost",
            groupId: dbGroup.Id,
            displayName: "Host",
            description: "Database server hostname",
            displayOrder: 0,
            allowedLevelIds: bothLevelIds,
            options: null,
            cancellationToken: cancellationToken);

        // DatabaseConnection.Port
        await SeedDefinitionAsync(
            key: "DatabaseConnection.Port",
            dataType: SettingDataType.Int,
            defaultValue: "5432",
            groupId: dbGroup.Id,
            displayName: "Port",
            description: "Database server port",
            displayOrder: 1,
            allowedLevelIds: bothLevelIds,
            options: null,
            cancellationToken: cancellationToken);

        // DatabaseConnection.Database
        await SeedDefinitionAsync(
            key: "DatabaseConnection.Database",
            dataType: SettingDataType.String,
            defaultValue: "app",
            groupId: dbGroup.Id,
            displayName: "Database",
            description: "Database name",
            displayOrder: 2,
            allowedLevelIds: bothLevelIds,
            options: null,
            cancellationToken: cancellationToken);
    }

    private async Task<SettingLevel> SeedLevelAsync(
        string name,
        Guid? parentId,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Set<SettingLevel>()
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var entity = new SettingLevel
        {
            Name = name,
            ParentId = parentId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingLevel>().Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    private async Task<SettingGroup> SeedGroupAsync(
        string key,
        string displayName,
        string? description,
        string? icon,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Set<SettingGroup>()
            .FirstOrDefaultAsync(g => g.Key == key, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var entity = new SettingGroup
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Icon = icon,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingGroup>().Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    private async Task SeedDefinitionAsync(
        string key,
        SettingDataType dataType,
        string? defaultValue,
        Guid? groupId,
        string displayName,
        string? description,
        int displayOrder,
        Guid[] allowedLevelIds,
        (string Value, string Label, int DisplayOrder, bool IsDefault)[]? options,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Set<SettingDefinition>()
            .FirstOrDefaultAsync(d => d.Key == key, cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var entity = new SettingDefinition
        {
            Key = key,
            DataType = dataType,
            DefaultValue = defaultValue,
            GroupId = groupId,
            DisplayName = displayName,
            Description = description,
            DisplayOrder = displayOrder,
            IsVisible = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingDefinition>().Add(entity);

        // Add options
        if (options is not null)
        {
            foreach (var (value, label, order, isDefault) in options)
            {
                var option = new SettingOption
                {
                    SettingDefinitionId = entity.Id,
                    Value = value,
                    Label = label,
                    DisplayOrder = order,
                    IsDefault = isDefault
                };
                _dbContext.Set<SettingOption>().Add(option);
            }
        }

        // Add allowed levels
        foreach (var levelId in allowedLevelIds)
        {
            var defLevel = new SettingDefinitionLevel
            {
                SettingDefinitionId = entity.Id,
                SettingLevelId = levelId
            };
            _dbContext.Set<SettingDefinitionLevel>().Add(defLevel);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
