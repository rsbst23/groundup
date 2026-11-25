using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;

namespace GroundUp.Data.Abstractions;

/// <summary>
/// Data access for the settings runtime: setting levels, definitions and values, with the query shapes
/// the cascading resolution needs (lookups by key and name, scope chain walks, batch value reads).
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Returns the id of the level with this exact name, or null.
    /// </summary>
    /// <param name="name">The level name (for example "System").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Guid?> GetLevelIdByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the name of the level with this id, or null.
    /// </summary>
    /// <param name="levelId">The level id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> GetLevelNameByIdAsync(Guid levelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the level with this exact name, creating it (with the given parent and display order)
    /// when it does not exist. An existing level is returned unchanged.
    /// </summary>
    /// <param name="name">The level name.</param>
    /// <param name="parentId">The parent level id used when creating; null for a root level.</param>
    /// <param name="displayOrder">The display order used when creating.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingLevelDto> EnsureLevelAsync(
        string name, Guid? parentId, int displayOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the definition with this key, including its allowed levels and options, or null.
    /// </summary>
    /// <param name="key">The definition key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingDefinitionDto?> GetDefinitionByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every definition, including allowed levels and options. Order is not defined.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingDefinitionDto>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the group with this key, or null.
    /// </summary>
    /// <param name="groupKey">The group key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingGroupDto?> GetGroupByKeyAsync(string groupKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the definitions in a group, including allowed levels and options. Order is not defined.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingDefinitionDto>> GetDefinitionsByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored value for a definition at one level and scope, or null.
    /// </summary>
    /// <param name="definitionId">The definition id.</param>
    /// <param name="levelId">The level id.</param>
    /// <param name="scopeId">The scope id (for example the tenant id); null for system-wide values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingValueDto?> GetValueAsync(
        Guid definitionId, Guid levelId, Guid? scopeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every stored value of the given definitions, at all levels and scopes, in one query.
    /// </summary>
    /// <param name="definitionIds">The definition ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SettingValueDto>> GetValuesForDefinitionsAsync(
        IReadOnlyCollection<Guid> definitionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the value of a definition at one level and scope. When a concurrent caller
    /// inserted the same value first (unique-constraint violation), the write is applied as an update.
    /// </summary>
    /// <param name="definitionId">The definition id.</param>
    /// <param name="levelId">The level id.</param>
    /// <param name="scopeId">The scope id; null for system-wide values.</param>
    /// <param name="value">The value to store (already encrypted when the setting is encrypted).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved value and the stored value it replaced.</returns>
    Task<SettingValueChangeDto> UpsertValueAsync(
        Guid definitionId, Guid levelId, Guid? scopeId, string? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored value with this id and the key of its definition, or null.
    /// </summary>
    /// <param name="valueId">The value id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SettingValueWithKeyDto?> FindValueAsync(Guid valueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the stored value with this id.
    /// </summary>
    /// <param name="valueId">The value id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the value does not exist (for example, deleted concurrently).</returns>
    Task<bool> DeleteValueByIdAsync(Guid valueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the definition with the request's key, creating it when missing: the group (when missing),
    /// the definition and its allowed levels (matched by name, case-insensitive) in one transaction.
    /// An existing definition is returned unchanged. When a concurrent caller creates the same definition
    /// first (unique-constraint violation), the winner's definition is returned.
    /// </summary>
    /// <param name="request">The definition to ensure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The definition; 500 when the creation failed and no definition exists afterwards.</returns>
    Task<OperationResult<SettingDefinitionDto>> EnsureDefinitionAsync(
        EnsureSettingDefinitionRequest request, CancellationToken cancellationToken = default);
}
