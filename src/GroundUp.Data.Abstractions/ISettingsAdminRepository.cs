using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;

namespace GroundUp.Data.Abstractions;

/// <summary>
/// Data access for administering settings metadata: levels, groups and definitions (with their
/// options and allowed levels). Writes return 404 when the target does not exist and 409
/// <c>CONFLICT</c> when a unique key (level name, group key, definition key) is already taken.
/// </summary>
public interface ISettingsAdminRepository
{
    #region Levels

    /// <summary>Returns all levels ordered by display order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingLevelDto>> GetAllLevelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the level with this id, or null.</summary>
    /// <param name="id">The level id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingLevelDto?> GetLevelByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when a level with this exact name exists.</summary>
    /// <param name="name">The level name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> LevelNameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>True when another level has this level as its parent.</summary>
    /// <param name="id">The level id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> LevelHasChildrenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when any setting value is stored at this level.</summary>
    /// <param name="id">The level id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> LevelHasValuesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates a level.</summary>
    /// <param name="dto">The new level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created level; 409 when the name is taken.</returns>
    Task<OperationResult<SettingLevelDto>> CreateLevelAsync(CreateSettingLevelDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a level.</summary>
    /// <param name="id">The level id.</param>
    /// <param name="dto">The new values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated level; 404 when missing; 409 when the name is taken.</returns>
    Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(Guid id, UpdateSettingLevelDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a level.</summary>
    /// <param name="id">The level id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok; 404 when missing.</returns>
    Task<OperationResult> DeleteLevelAsync(Guid id, CancellationToken cancellationToken = default);

    #endregion

    #region Groups

    /// <summary>Returns all groups ordered by display order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingGroupDto>> GetAllGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a group with this exact key exists.</summary>
    /// <param name="key">The group key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> GroupKeyExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates a group.</summary>
    /// <param name="dto">The new group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created group; 409 when the key is taken.</returns>
    Task<OperationResult<SettingGroupDto>> CreateGroupAsync(CreateSettingGroupDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a group.</summary>
    /// <param name="id">The group id.</param>
    /// <param name="dto">The new values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated group; 404 when missing; 409 when the key is taken.</returns>
    Task<OperationResult<SettingGroupDto>> UpdateGroupAsync(Guid id, UpdateSettingGroupDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a group. Its definitions are kept and no longer belong to a group (same save).</summary>
    /// <param name="id">The group id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok; 404 when missing.</returns>
    Task<OperationResult> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    #endregion

    #region Definitions

    /// <summary>Returns all definitions with allowed levels and options, ordered by display order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingDefinitionDto>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the definition with this id (with allowed levels and options), or null.</summary>
    /// <param name="id">The definition id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingDefinitionDto?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when a definition with this exact key exists.</summary>
    /// <param name="key">The definition key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> DefinitionKeyExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates a definition with its options and allowed levels in one save.</summary>
    /// <param name="dto">The new definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created definition; 409 when the key is taken.</returns>
    Task<OperationResult<SettingDefinitionDto>> CreateDefinitionAsync(CreateSettingDefinitionDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a definition, replacing all of its options and allowed levels, in one save.</summary>
    /// <param name="id">The definition id.</param>
    /// <param name="dto">The new values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated definition; 404 when missing; 409 when the key is taken.</returns>
    Task<OperationResult<SettingDefinitionDto>> UpdateDefinitionAsync(Guid id, UpdateSettingDefinitionDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a definition (its options, level links and values go with it).</summary>
    /// <param name="id">The definition id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ok; 404 when missing.</returns>
    Task<OperationResult> DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    #endregion
}
