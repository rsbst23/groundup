using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Services.Settings;

/// <summary>
/// Sealed, scoped implementation of <see cref="ISettingsAdminService"/> that performs
/// CRUD operations on settings metadata entities (levels, groups, definitions).
/// Uses EF Core directly via <c>DbContext.Set&lt;T&gt;()</c> for optimized query patterns.
/// </summary>
public sealed class SettingsAdminService : ISettingsAdminService
{
    private readonly GroundUpDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsAdminService"/>.
    /// </summary>
    /// <param name="dbContext">The EF Core database context for querying and persisting settings metadata.</param>
    public SettingsAdminService(GroundUpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #region Levels

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingLevelDto>>> GetAllLevelsAsync(
        CancellationToken cancellationToken = default)
    {
        var levels = await _dbContext.Set<SettingLevel>()
            .AsNoTracking()
            .OrderBy(l => l.DisplayOrder)
            .ToListAsync(cancellationToken);

        var dtos = levels.Select(MapToLevelDto).ToList();
        return OperationResult<IReadOnlyList<SettingLevelDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingLevelDto>> CreateLevelAsync(
        CreateSettingLevelDto dto,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Set<SettingLevel>()
            .AnyAsync(l => l.Name == dto.Name, cancellationToken);

        if (exists)
        {
            return OperationResult<SettingLevelDto>.BadRequest(
                $"A setting level with name '{dto.Name}' already exists");
        }

        var entity = new SettingLevel
        {
            Name = dto.Name,
            Description = dto.Description,
            ParentId = dto.ParentId,
            DisplayOrder = dto.DisplayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingLevel>().Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingLevelDto>.Ok(MapToLevelDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(
        Guid id,
        UpdateSettingLevelDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingLevel>()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult<SettingLevelDto>.NotFound($"Setting level '{id}' not found");
        }

        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.ParentId = dto.ParentId;
        entity.DisplayOrder = dto.DisplayOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingLevelDto>.Ok(MapToLevelDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteLevelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingLevel>()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult.NotFound($"Setting level '{id}' not found");
        }

        // Check for child levels
        var hasChildren = await _dbContext.Set<SettingLevel>()
            .AnyAsync(l => l.ParentId == id, cancellationToken);

        if (hasChildren)
        {
            return OperationResult.BadRequest(
                $"Cannot delete level '{entity.Name}' because it has child levels");
        }

        // Check for referencing SettingValues
        var hasValues = await _dbContext.Set<SettingValue>()
            .AnyAsync(v => v.LevelId == id, cancellationToken);

        if (hasValues)
        {
            return OperationResult.BadRequest(
                $"Cannot delete level '{entity.Name}' because it is referenced by setting values");
        }

        _dbContext.Set<SettingLevel>().Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok();
    }

    #endregion

    #region Groups

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingGroupDto>>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await _dbContext.Set<SettingGroup>()
            .AsNoTracking()
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var dtos = groups.Select(MapToGroupDto).ToList();
        return OperationResult<IReadOnlyList<SettingGroupDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingGroupDto>> CreateGroupAsync(
        CreateSettingGroupDto dto,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Set<SettingGroup>()
            .AnyAsync(g => g.Key == dto.Key, cancellationToken);

        if (exists)
        {
            return OperationResult<SettingGroupDto>.BadRequest(
                $"A setting group with key '{dto.Key}' already exists");
        }

        var entity = new SettingGroup
        {
            Key = dto.Key,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Icon = dto.Icon,
            DisplayOrder = dto.DisplayOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingGroup>().Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingGroupDto>.Ok(MapToGroupDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingGroupDto>> UpdateGroupAsync(
        Guid id,
        UpdateSettingGroupDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingGroup>()
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult<SettingGroupDto>.NotFound($"Setting group '{id}' not found");
        }

        entity.Key = dto.Key;
        entity.DisplayName = dto.DisplayName;
        entity.Description = dto.Description;
        entity.Icon = dto.Icon;
        entity.DisplayOrder = dto.DisplayOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingGroupDto>.Ok(MapToGroupDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingGroup>()
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult.NotFound($"Setting group '{id}' not found");
        }

        // Orphan definitions: set GroupId to null
        var definitions = await _dbContext.Set<SettingDefinition>()
            .Where(d => d.GroupId == id)
            .ToListAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            definition.GroupId = null;
        }

        _dbContext.Set<SettingGroup>().Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok();
    }

    #endregion

    #region Definitions

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingDefinitionDto>>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = await _dbContext.Set<SettingDefinition>()
            .AsNoTracking()
            .Include(d => d.Options)
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync(cancellationToken);

        var dtos = definitions.Select(MapToDefinitionDto).ToList();
        return OperationResult<IReadOnlyList<SettingDefinitionDto>>.Ok(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> GetDefinitionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingDefinition>()
            .AsNoTracking()
            .Include(d => d.Options)
            .Include(d => d.AllowedLevels)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult<SettingDefinitionDto>.NotFound($"Setting definition '{id}' not found");
        }

        return OperationResult<SettingDefinitionDto>.Ok(MapToDefinitionDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> CreateDefinitionAsync(
        CreateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.Set<SettingDefinition>()
            .AnyAsync(d => d.Key == dto.Key, cancellationToken);

        if (exists)
        {
            return OperationResult<SettingDefinitionDto>.BadRequest(
                $"A setting definition with key '{dto.Key}' already exists");
        }

        var entity = new SettingDefinition
        {
            Key = dto.Key,
            DataType = dto.DataType,
            DefaultValue = dto.DefaultValue,
            GroupId = dto.GroupId,
            DisplayName = dto.DisplayName,
            Description = dto.Description,
            Placeholder = dto.Placeholder,
            Category = dto.Category,
            DisplayOrder = dto.DisplayOrder,
            IsVisible = dto.IsVisible,
            IsReadOnly = dto.IsReadOnly,
            AllowMultiple = dto.AllowMultiple,
            IsEncrypted = dto.IsEncrypted,
            IsSecret = dto.IsSecret,
            IsRequired = dto.IsRequired,
            MinValue = dto.MinValue,
            MaxValue = dto.MaxValue,
            MinLength = dto.MinLength,
            MaxLength = dto.MaxLength,
            RegexPattern = dto.RegexPattern,
            ValidationMessage = dto.ValidationMessage,
            DependsOnKey = dto.DependsOnKey,
            DependsOnOperator = dto.DependsOnOperator,
            DependsOnValue = dto.DependsOnValue,
            CustomValidatorType = dto.CustomValidatorType,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<SettingDefinition>().Add(entity);

        // Add options
        if (dto.Options is { Count: > 0 })
        {
            foreach (var optionDto in dto.Options)
            {
                var option = new SettingOption
                {
                    SettingDefinitionId = entity.Id,
                    Value = optionDto.Value,
                    Label = optionDto.Label,
                    DisplayOrder = optionDto.DisplayOrder,
                    IsDefault = optionDto.IsDefault,
                    ParentOptionValue = optionDto.ParentOptionValue
                };
                _dbContext.Set<SettingOption>().Add(option);
            }
        }

        // Add allowed levels
        if (dto.AllowedLevelIds is { Count: > 0 })
        {
            foreach (var levelId in dto.AllowedLevelIds)
            {
                var defLevel = new SettingDefinitionLevel
                {
                    SettingDefinitionId = entity.Id,
                    SettingLevelId = levelId
                };
                _dbContext.Set<SettingDefinitionLevel>().Add(defLevel);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingDefinitionDto>.Ok(MapToDefinitionDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> UpdateDefinitionAsync(
        Guid id,
        UpdateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingDefinition>()
            .Include(d => d.Options)
            .Include(d => d.AllowedLevels)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult<SettingDefinitionDto>.NotFound($"Setting definition '{id}' not found");
        }

        // Update scalar fields
        entity.Key = dto.Key;
        entity.DataType = dto.DataType;
        entity.DefaultValue = dto.DefaultValue;
        entity.GroupId = dto.GroupId;
        entity.DisplayName = dto.DisplayName;
        entity.Description = dto.Description;
        entity.Placeholder = dto.Placeholder;
        entity.Category = dto.Category;
        entity.DisplayOrder = dto.DisplayOrder;
        entity.IsVisible = dto.IsVisible;
        entity.IsReadOnly = dto.IsReadOnly;
        entity.AllowMultiple = dto.AllowMultiple;
        entity.IsEncrypted = dto.IsEncrypted;
        entity.IsSecret = dto.IsSecret;
        entity.IsRequired = dto.IsRequired;
        entity.MinValue = dto.MinValue;
        entity.MaxValue = dto.MaxValue;
        entity.MinLength = dto.MinLength;
        entity.MaxLength = dto.MaxLength;
        entity.RegexPattern = dto.RegexPattern;
        entity.ValidationMessage = dto.ValidationMessage;
        entity.DependsOnKey = dto.DependsOnKey;
        entity.DependsOnOperator = dto.DependsOnOperator;
        entity.DependsOnValue = dto.DependsOnValue;
        entity.CustomValidatorType = dto.CustomValidatorType;
        entity.UpdatedAt = DateTime.UtcNow;

        // Full replace of options: remove existing, add new
        _dbContext.Set<SettingOption>().RemoveRange(entity.Options);

        if (dto.Options is { Count: > 0 })
        {
            foreach (var optionDto in dto.Options)
            {
                var option = new SettingOption
                {
                    SettingDefinitionId = entity.Id,
                    Value = optionDto.Value,
                    Label = optionDto.Label,
                    DisplayOrder = optionDto.DisplayOrder,
                    IsDefault = optionDto.IsDefault,
                    ParentOptionValue = optionDto.ParentOptionValue
                };
                _dbContext.Set<SettingOption>().Add(option);
            }
        }

        // Full replace of allowed levels: remove existing, add new
        _dbContext.Set<SettingDefinitionLevel>().RemoveRange(entity.AllowedLevels);

        if (dto.AllowedLevelIds is { Count: > 0 })
        {
            foreach (var levelId in dto.AllowedLevelIds)
            {
                var defLevel = new SettingDefinitionLevel
                {
                    SettingDefinitionId = entity.Id,
                    SettingLevelId = levelId
                };
                _dbContext.Set<SettingDefinitionLevel>().Add(defLevel);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<SettingDefinitionDto>.Ok(MapToDefinitionDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<SettingDefinition>()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult.NotFound($"Setting definition '{id}' not found");
        }

        _dbContext.Set<SettingDefinition>().Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult.Ok();
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Maps a <see cref="SettingLevel"/> entity to a <see cref="SettingLevelDto"/>.
    /// </summary>
    private static SettingLevelDto MapToLevelDto(SettingLevel entity) => new(
        entity.Id,
        entity.Name,
        entity.Description,
        entity.ParentId,
        entity.DisplayOrder);

    /// <summary>
    /// Maps a <see cref="SettingGroup"/> entity to a <see cref="SettingGroupDto"/>.
    /// </summary>
    private static SettingGroupDto MapToGroupDto(SettingGroup entity) => new(
        entity.Id,
        entity.Key,
        entity.DisplayName,
        entity.Description,
        entity.Icon,
        entity.DisplayOrder);

    /// <summary>
    /// Maps a <see cref="SettingDefinition"/> entity to a <see cref="SettingDefinitionDto"/>.
    /// </summary>
    private static SettingDefinitionDto MapToDefinitionDto(SettingDefinition entity) => new(
        entity.Id,
        entity.Key,
        entity.DataType,
        entity.DefaultValue,
        entity.GroupId,
        entity.DisplayName,
        entity.Description,
        entity.Placeholder,
        entity.Category,
        entity.DisplayOrder,
        entity.IsVisible,
        entity.IsReadOnly,
        entity.AllowMultiple,
        entity.IsEncrypted,
        entity.IsSecret,
        entity.IsRequired,
        entity.MinValue,
        entity.MaxValue,
        entity.MinLength,
        entity.MaxLength,
        entity.RegexPattern,
        entity.ValidationMessage,
        entity.DependsOnKey,
        entity.DependsOnOperator,
        entity.DependsOnValue,
        entity.CustomValidatorType);

    #endregion
}
