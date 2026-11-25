using GroundUp.Core;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Repositories.Mappers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories.Settings;

/// <summary>
/// <see cref="ISettingsAdminRepository"/> over the application's DbContext. Database-agnostic: unique
/// violations are recognized through <see cref="IUniqueConstraintDetector"/>.
/// </summary>
public sealed class SettingsAdminRepository : ISettingsAdminRepository
{
    private readonly DbContext _context;
    private readonly IUniqueConstraintDetector _uniqueConstraintDetector;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsAdminRepository"/> class.
    /// </summary>
    /// <param name="context">The application's DbContext (contains the settings entities).</param>
    /// <param name="uniqueConstraintDetector">Recognizes unique-constraint violations from the provider.</param>
    public SettingsAdminRepository(DbContext context, IUniqueConstraintDetector uniqueConstraintDetector)
    {
        _context = context;
        _uniqueConstraintDetector = uniqueConstraintDetector;
    }

    private DbSet<SettingLevel> Levels => _context.Set<SettingLevel>();
    private DbSet<SettingGroup> Groups => _context.Set<SettingGroup>();
    private DbSet<SettingDefinition> Definitions => _context.Set<SettingDefinition>();

    #region Levels

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingLevelDto>> GetAllLevelsAsync(CancellationToken cancellationToken = default)
    {
        var levels = await Levels.AsNoTracking().OrderBy(l => l.DisplayOrder).ToListAsync(cancellationToken);
        return levels.Select(SettingLevelMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SettingLevelDto?> GetLevelByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var level = await Levels.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return level is null ? null : SettingLevelMapper.ToDto(level);
    }

    /// <inheritdoc />
    public Task<bool> LevelNameExistsAsync(string name, CancellationToken cancellationToken = default)
        => Levels.AnyAsync(l => l.Name == name, cancellationToken);

    /// <inheritdoc />
    public Task<bool> LevelHasChildrenAsync(Guid id, CancellationToken cancellationToken = default)
        => Levels.AnyAsync(l => l.ParentId == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> LevelHasValuesAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Set<SettingValue>().AnyAsync(v => v.LevelId == id, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<SettingLevelDto>> CreateLevelAsync(
        CreateSettingLevelDto dto, CancellationToken cancellationToken = default)
    {
        var entity = SettingLevelMapper.ToEntity(dto);
        entity.CreatedAt = DateTime.UtcNow;
        Levels.Add(entity);

        var conflict = await SaveAsync<SettingLevelDto>(
            $"A setting level with name '{dto.Name}' already exists", cancellationToken);
        return conflict ?? OperationResult<SettingLevelDto>.Ok(SettingLevelMapper.ToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(
        Guid id, UpdateSettingLevelDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await Levels.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (entity is null)
        {
            return OperationResult<SettingLevelDto>.NotFound($"Setting level '{id}' not found");
        }

        SettingLevelMapper.UpdateEntity(dto, entity);
        entity.UpdatedAt = DateTime.UtcNow;

        var conflict = await SaveAsync<SettingLevelDto>(
            $"A setting level with name '{dto.Name}' already exists", cancellationToken);
        return conflict ?? OperationResult<SettingLevelDto>.Ok(SettingLevelMapper.ToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteLevelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Levels.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (entity is null)
        {
            return OperationResult.NotFound($"Setting level '{id}' not found");
        }

        Levels.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok();
    }

    #endregion

    #region Groups

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingGroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await Groups.AsNoTracking().OrderBy(g => g.DisplayOrder).ToListAsync(cancellationToken);
        return groups.Select(SettingGroupMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public Task<bool> GroupKeyExistsAsync(string key, CancellationToken cancellationToken = default)
        => Groups.AnyAsync(g => g.Key == key, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<SettingGroupDto>> CreateGroupAsync(
        CreateSettingGroupDto dto, CancellationToken cancellationToken = default)
    {
        var entity = SettingGroupMapper.ToEntity(dto);
        entity.CreatedAt = DateTime.UtcNow;
        Groups.Add(entity);

        var conflict = await SaveAsync<SettingGroupDto>(
            $"A setting group with key '{dto.Key}' already exists", cancellationToken);
        return conflict ?? OperationResult<SettingGroupDto>.Ok(SettingGroupMapper.ToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingGroupDto>> UpdateGroupAsync(
        Guid id, UpdateSettingGroupDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (entity is null)
        {
            return OperationResult<SettingGroupDto>.NotFound($"Setting group '{id}' not found");
        }

        SettingGroupMapper.UpdateEntity(dto, entity);
        entity.UpdatedAt = DateTime.UtcNow;

        var conflict = await SaveAsync<SettingGroupDto>(
            $"A setting group with key '{dto.Key}' already exists", cancellationToken);
        return conflict ?? OperationResult<SettingGroupDto>.Ok(SettingGroupMapper.ToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (entity is null)
        {
            return OperationResult.NotFound($"Setting group '{id}' not found");
        }

        // Orphan definitions: they stay, without a group
        var definitions = await Definitions.Where(d => d.GroupId == id).ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            definition.GroupId = null;
        }

        Groups.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok();
    }

    #endregion

    #region Definitions

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingDefinitionDto>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await DefinitionsWithDetails()
            .OrderBy(d => d.DisplayOrder)
            .ToListAsync(cancellationToken);
        return definitions.Select(SettingDefinitionMapper.ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<SettingDefinitionDto?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await DefinitionsWithDetails().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? null : SettingDefinitionMapper.ToDto(definition);
    }

    /// <inheritdoc />
    public Task<bool> DefinitionKeyExistsAsync(string key, CancellationToken cancellationToken = default)
        => Definitions.AnyAsync(d => d.Key == key, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> CreateDefinitionAsync(
        CreateSettingDefinitionDto dto, CancellationToken cancellationToken = default)
    {
        var entity = SettingDefinitionMapper.ToEntity(dto);
        entity.CreatedAt = DateTime.UtcNow;
        Definitions.Add(entity);
        AddOptionsAndLevels(entity.Id, dto.Options, dto.AllowedLevelIds);

        var conflict = await SaveAsync<SettingDefinitionDto>(
            $"A setting definition with key '{dto.Key}' already exists", cancellationToken);
        return conflict ?? await ReadDefinitionAsync(entity.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> UpdateDefinitionAsync(
        Guid id, UpdateSettingDefinitionDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await Definitions
            .Include(d => d.Options)
            .Include(d => d.AllowedLevels)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (entity is null)
        {
            return OperationResult<SettingDefinitionDto>.NotFound($"Setting definition '{id}' not found");
        }

        SettingDefinitionMapper.UpdateEntity(dto, entity);
        entity.UpdatedAt = DateTime.UtcNow;

        // Full replace of options and allowed levels
        _context.Set<SettingOption>().RemoveRange(entity.Options);
        _context.Set<SettingDefinitionLevel>().RemoveRange(entity.AllowedLevels);
        AddOptionsAndLevels(entity.Id, dto.Options, dto.AllowedLevelIds);

        var conflict = await SaveAsync<SettingDefinitionDto>(
            $"A setting definition with key '{dto.Key}' already exists", cancellationToken);
        return conflict ?? await ReadDefinitionAsync(entity.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Definitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null)
        {
            return OperationResult.NotFound($"Setting definition '{id}' not found");
        }

        Definitions.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok();
    }

    private IQueryable<SettingDefinition> DefinitionsWithDetails() => Definitions
        .AsNoTracking()
        .Include(d => d.AllowedLevels)
        .Include(d => d.Options)
        .AsSplitQuery();

    private void AddOptionsAndLevels(
        Guid definitionId,
        IReadOnlyList<CreateSettingOptionDto>? options,
        IReadOnlyList<Guid>? allowedLevelIds)
    {
        foreach (var option in options ?? [])
        {
            _context.Set<SettingOption>().Add(SettingDefinitionMapper.ToEntity(option, definitionId));
        }

        // Distinct: a repeated level id would hit the (definition, level) unique index
        foreach (var levelId in (allowedLevelIds ?? []).Distinct())
        {
            _context.Set<SettingDefinitionLevel>().Add(new SettingDefinitionLevel
            {
                SettingDefinitionId = definitionId,
                SettingLevelId = levelId
            });
        }
    }

    private async Task<OperationResult<SettingDefinitionDto>> ReadDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var saved = await GetDefinitionByIdAsync(id, cancellationToken);
        return saved is not null
            ? OperationResult<SettingDefinitionDto>.Ok(saved)
            : OperationResult<SettingDefinitionDto>.NotFound($"Setting definition '{id}' not found");
    }

    #endregion

    /// <summary>
    /// Saves pending changes. On a unique-constraint violation, drops the pending changes and returns
    /// 409 <c>CONFLICT</c> with <paramref name="conflictMessage"/>; otherwise returns null.
    /// </summary>
    private async Task<OperationResult<T>?> SaveAsync<T>(string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (_uniqueConstraintDetector.IsUniqueConstraintViolation(ex))
        {
            _context.ChangeTracker.Clear();
            return OperationResult<T>.Fail(conflictMessage, 409, ErrorCodes.Conflict);
        }
    }
}
