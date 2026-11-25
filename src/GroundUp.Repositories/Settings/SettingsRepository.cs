using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Repositories.Mappers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories.Settings;

/// <summary>
/// <see cref="ISettingsRepository"/> over the application's DbContext. Database-agnostic: unique
/// violations are recognized through <see cref="IUniqueConstraintDetector"/>.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly DbContext _context;
    private readonly IUniqueConstraintDetector _uniqueConstraintDetector;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsRepository"/> class.
    /// </summary>
    /// <param name="context">The application's DbContext (contains the settings entities).</param>
    /// <param name="uniqueConstraintDetector">Recognizes unique-constraint violations from the provider.</param>
    public SettingsRepository(DbContext context, IUniqueConstraintDetector uniqueConstraintDetector)
    {
        _context = context;
        _uniqueConstraintDetector = uniqueConstraintDetector;
    }

    private DbSet<SettingLevel> Levels => _context.Set<SettingLevel>();
    private DbSet<SettingDefinition> Definitions => _context.Set<SettingDefinition>();
    private DbSet<SettingGroup> Groups => _context.Set<SettingGroup>();
    private DbSet<SettingValue> Values => _context.Set<SettingValue>();

    /// <summary>Definitions with the collections <see cref="SettingDefinitionDto"/> carries, read-only.</summary>
    private IQueryable<SettingDefinition> DefinitionsWithDetails => Definitions
        .AsNoTracking()
        .Include(d => d.AllowedLevels)
        .Include(d => d.Options)
        .AsSplitQuery();

    #region Levels

    /// <inheritdoc />
    public async Task<Guid?> GetLevelIdByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await Levels
            .AsNoTracking()
            .Where(l => l.Name == name)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> GetLevelNameByIdAsync(Guid levelId, CancellationToken cancellationToken = default)
    {
        return await Levels
            .AsNoTracking()
            .Where(l => l.Id == levelId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SettingLevelDto> EnsureLevelAsync(
        string name, Guid? parentId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var existing = await Levels
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == name, cancellationToken);

        if (existing is not null)
        {
            return SettingLevelMapper.ToDto(existing);
        }

        var level = new SettingLevel
        {
            Name = name,
            ParentId = parentId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

        Levels.Add(level);
        await _context.SaveChangesAsync(cancellationToken);

        return SettingLevelMapper.ToDto(level);
    }

    #endregion

    #region Definitions and groups

    /// <inheritdoc />
    public async Task<SettingDefinitionDto?> GetDefinitionByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = await DefinitionsWithDetails.FirstOrDefaultAsync(d => d.Key == key, cancellationToken);
        return definition is null ? null : SettingDefinitionMapper.ToDto(definition);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingDefinitionDto>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await DefinitionsWithDetails.ToListAsync(cancellationToken);
        return definitions.Select(SettingDefinitionMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SettingGroupDto?> GetGroupByKeyAsync(string groupKey, CancellationToken cancellationToken = default)
    {
        var group = await Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Key == groupKey, cancellationToken);
        return group is null ? null : SettingGroupMapper.ToDto(group);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsByGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default)
    {
        var definitions = await DefinitionsWithDetails
            .Where(d => d.GroupId == groupId)
            .ToListAsync(cancellationToken);
        return definitions.Select(SettingDefinitionMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> EnsureDefinitionAsync(
        EnsureSettingDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await GetDefinitionByKeyAsync(request.Key, cancellationToken);
        if (existing is not null)
        {
            return OperationResult<SettingDefinitionDto>.Ok(existing);
        }

        // One transaction for group + definition + level links
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var group = await Groups.FirstOrDefaultAsync(g => g.Key == request.GroupKey, cancellationToken);
            if (group is null)
            {
                group = new SettingGroup
                {
                    Key = request.GroupKey,
                    DisplayName = request.GroupDisplayName,
                    DisplayOrder = 0
                };
                Groups.Add(group);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Resolve level ids from names (case-insensitive)
            var levelNames = request.AllowedLevelNames
                .Select(n => n.ToLowerInvariant())
                .ToList();
            var levelIds = await Levels
                .AsNoTracking()
                .Where(l => levelNames.Contains(l.Name.ToLower()))
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            var definition = new SettingDefinition
            {
                Key = request.Key,
                DataType = request.DataType,
                DefaultValue = request.DefaultValue,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Category = request.Category,
                GroupId = group.Id,
                RegexPattern = request.RegexPattern,
                ValidationMessage = request.ValidationMessage,
                IsRequired = request.IsRequired,
                IsSecret = request.IsSecret,
                IsEncrypted = request.IsEncrypted,
                MinValue = request.MinValue,
                MaxValue = request.MaxValue,
                IsVisible = true,
                IsReadOnly = false,
                AllowMultiple = false,
                DisplayOrder = 0
            };

            Definitions.Add(definition);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var levelId in levelIds)
            {
                _context.Set<SettingDefinitionLevel>().Add(new SettingDefinitionLevel
                {
                    SettingDefinitionId = definition.Id,
                    SettingLevelId = levelId
                });
            }

            if (levelIds.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_uniqueConstraintDetector.IsUniqueConstraintViolation(ex))
        {
            // A concurrent caller created the group or definition first. Drop this attempt and
            // return the winner's definition.
            await transaction.RollbackAsync(cancellationToken);
            _context.ChangeTracker.Clear();
        }

        var result = await GetDefinitionByKeyAsync(request.Key, cancellationToken);
        return result is not null
            ? OperationResult<SettingDefinitionDto>.Ok(result)
            : OperationResult<SettingDefinitionDto>.Fail($"Failed to create setting definition '{request.Key}'", 500);
    }

    #endregion

    #region Values

    /// <inheritdoc />
    public async Task<SettingValueDto?> GetValueAsync(
        Guid definitionId, Guid levelId, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        var value = await Values
            .AsNoTracking()
            .FirstOrDefaultAsync(v =>
                v.SettingDefinitionId == definitionId &&
                v.LevelId == levelId &&
                v.ScopeId == scopeId,
                cancellationToken);
        return value is null ? null : SettingValueMapper.ToDto(value);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingValueDto>> GetValuesForDefinitionsAsync(
        IReadOnlyCollection<Guid> definitionIds, CancellationToken cancellationToken = default)
    {
        var values = await Values
            .AsNoTracking()
            .Where(v => definitionIds.Contains(v.SettingDefinitionId))
            .ToListAsync(cancellationToken);
        return values.Select(SettingValueMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SettingValueChangeDto> UpsertValueAsync(
        Guid definitionId, Guid levelId, Guid? scopeId, string? value, CancellationToken cancellationToken = default)
    {
        var entity = await FindTrackedValueAsync(definitionId, levelId, scopeId, cancellationToken);
        var inserted = entity is null;
        string? oldValue = null;

        if (entity is not null)
        {
            oldValue = entity.Value;
            entity.Value = value;
        }
        else
        {
            entity = new SettingValue
            {
                SettingDefinitionId = definitionId,
                LevelId = levelId,
                ScopeId = scopeId,
                Value = value
            };
            Values.Add(entity);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (inserted && _uniqueConstraintDetector.IsUniqueConstraintViolation(ex))
        {
            // A concurrent caller inserted the same value first: apply this write as an update of that row.
            _context.ChangeTracker.Clear();
            entity = await FindTrackedValueAsync(definitionId, levelId, scopeId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Setting value insert hit a unique constraint, but no existing value was found.", ex);
            oldValue = entity.Value;
            entity.Value = value;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new SettingValueChangeDto(SettingValueMapper.ToDto(entity), oldValue);
    }

    /// <inheritdoc />
    public async Task<SettingValueWithKeyDto?> FindValueAsync(Guid valueId, CancellationToken cancellationToken = default)
    {
        var value = await Values
            .AsNoTracking()
            .Include(v => v.SettingDefinition)
            .FirstOrDefaultAsync(v => v.Id == valueId, cancellationToken);

        return value is null
            ? null
            : new SettingValueWithKeyDto(SettingValueMapper.ToDto(value), value.SettingDefinition.Key);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteValueByIdAsync(Guid valueId, CancellationToken cancellationToken = default)
    {
        var value = await Values.FirstOrDefaultAsync(v => v.Id == valueId, cancellationToken);
        if (value is null)
        {
            return false;
        }

        Values.Remove(value);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<SettingValue?> FindTrackedValueAsync(
        Guid definitionId, Guid levelId, Guid? scopeId, CancellationToken cancellationToken)
        => Values.FirstOrDefaultAsync(v =>
            v.SettingDefinitionId == definitionId &&
            v.LevelId == levelId &&
            v.ScopeId == scopeId,
            cancellationToken);

    #endregion
}
