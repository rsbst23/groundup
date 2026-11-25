using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Services.Settings;

/// <summary>
/// Sealed, scoped implementation of <see cref="ISettingsAdminService"/> that performs
/// CRUD operations on settings metadata (levels, groups, definitions). Business rules (duplicate
/// checks, delete guards) live here; data access goes through <see cref="ISettingsAdminRepository"/>.
/// </summary>
public sealed class SettingsAdminService : ISettingsAdminService
{
    private readonly ISettingsAdminRepository _repository;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsAdminService"/>.
    /// </summary>
    /// <param name="repository">Data access for settings metadata.</param>
    public SettingsAdminService(ISettingsAdminRepository repository)
    {
        _repository = repository;
    }

    #region Levels

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingLevelDto>>> GetAllLevelsAsync(
        CancellationToken cancellationToken = default)
    {
        var levels = await _repository.GetAllLevelsAsync(cancellationToken);
        return OperationResult<IReadOnlyList<SettingLevelDto>>.Ok(levels);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingLevelDto>> CreateLevelAsync(
        CreateSettingLevelDto dto,
        CancellationToken cancellationToken = default)
    {
        if (await _repository.LevelNameExistsAsync(dto.Name, cancellationToken))
        {
            return OperationResult<SettingLevelDto>.BadRequest(
                $"A setting level with name '{dto.Name}' already exists");
        }

        return await _repository.CreateLevelAsync(dto, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(
        Guid id,
        UpdateSettingLevelDto dto,
        CancellationToken cancellationToken = default)
        => _repository.UpdateLevelAsync(id, dto, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult> DeleteLevelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var level = await _repository.GetLevelByIdAsync(id, cancellationToken);

        if (level is null)
        {
            return OperationResult.NotFound($"Setting level '{id}' not found");
        }

        if (await _repository.LevelHasChildrenAsync(id, cancellationToken))
        {
            return OperationResult.BadRequest(
                $"Cannot delete level '{level.Name}' because it has child levels");
        }

        if (await _repository.LevelHasValuesAsync(id, cancellationToken))
        {
            return OperationResult.BadRequest(
                $"Cannot delete level '{level.Name}' because it is referenced by setting values");
        }

        return await _repository.DeleteLevelAsync(id, cancellationToken);
    }

    #endregion

    #region Groups

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingGroupDto>>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await _repository.GetAllGroupsAsync(cancellationToken);
        return OperationResult<IReadOnlyList<SettingGroupDto>>.Ok(groups);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingGroupDto>> CreateGroupAsync(
        CreateSettingGroupDto dto,
        CancellationToken cancellationToken = default)
    {
        if (await _repository.GroupKeyExistsAsync(dto.Key, cancellationToken))
        {
            return OperationResult<SettingGroupDto>.BadRequest(
                $"A setting group with key '{dto.Key}' already exists");
        }

        return await _repository.CreateGroupAsync(dto, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<SettingGroupDto>> UpdateGroupAsync(
        Guid id,
        UpdateSettingGroupDto dto,
        CancellationToken cancellationToken = default)
        => _repository.UpdateGroupAsync(id, dto, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _repository.DeleteGroupAsync(id, cancellationToken);

    #endregion

    #region Definitions

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingDefinitionDto>>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = await _repository.GetAllDefinitionsAsync(cancellationToken);
        return OperationResult<IReadOnlyList<SettingDefinitionDto>>.Ok(definitions);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> GetDefinitionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var definition = await _repository.GetDefinitionByIdAsync(id, cancellationToken);

        return definition is null
            ? OperationResult<SettingDefinitionDto>.NotFound($"Setting definition '{id}' not found")
            : OperationResult<SettingDefinitionDto>.Ok(definition);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingDefinitionDto>> CreateDefinitionAsync(
        CreateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (await _repository.DefinitionKeyExistsAsync(dto.Key, cancellationToken))
        {
            return OperationResult<SettingDefinitionDto>.BadRequest(
                $"A setting definition with key '{dto.Key}' already exists");
        }

        return await _repository.CreateDefinitionAsync(dto, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<SettingDefinitionDto>> UpdateDefinitionAsync(
        Guid id,
        UpdateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default)
        => _repository.UpdateDefinitionAsync(id, dto, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult> DeleteDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _repository.DeleteDefinitionAsync(id, cancellationToken);

    #endregion
}
