using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;

namespace GroundUp.Core.Abstractions;

/// <summary>
/// Service interface for CRUD operations on settings metadata entities
/// (levels, groups, definitions). Separated from <see cref="ISettingsService"/>
/// which focuses on cascade resolution. Admin operations are typically exposed
/// only in back-office or developer tooling scenarios.
/// </summary>
public interface ISettingsAdminService
{
    #region Levels

    /// <summary>
    /// Gets all setting levels ordered by display order.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing all setting levels.</returns>
    Task<OperationResult<IReadOnlyList<SettingLevelDto>>> GetAllLevelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new setting level.
    /// </summary>
    /// <param name="dto">The level creation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the created level.</returns>
    Task<OperationResult<SettingLevelDto>> CreateLevelAsync(
        CreateSettingLevelDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing setting level.
    /// </summary>
    /// <param name="id">The unique identifier of the level to update.</param>
    /// <param name="dto">The level update data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the updated level.</returns>
    Task<OperationResult<SettingLevelDto>> UpdateLevelAsync(
        Guid id,
        UpdateSettingLevelDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting level. Returns <see cref="OperationResult.BadRequest"/>
    /// if the level has child levels or is referenced by setting values.
    /// </summary>
    /// <param name="id">The unique identifier of the level to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    Task<OperationResult> DeleteLevelAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    #endregion

    #region Groups

    /// <summary>
    /// Gets all setting groups ordered by display order.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing all setting groups.</returns>
    Task<OperationResult<IReadOnlyList<SettingGroupDto>>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new setting group.
    /// </summary>
    /// <param name="dto">The group creation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the created group.</returns>
    Task<OperationResult<SettingGroupDto>> CreateGroupAsync(
        CreateSettingGroupDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing setting group.
    /// </summary>
    /// <param name="id">The unique identifier of the group to update.</param>
    /// <param name="dto">The group update data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the updated group.</returns>
    Task<OperationResult<SettingGroupDto>> UpdateGroupAsync(
        Guid id,
        UpdateSettingGroupDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting group. Definitions in the group are orphaned
    /// (their GroupId is set to null) rather than deleted.
    /// </summary>
    /// <param name="id">The unique identifier of the group to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    Task<OperationResult> DeleteGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    #endregion

    #region Definitions

    /// <summary>
    /// Gets all setting definitions with their associated options.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing all setting definitions.</returns>
    Task<OperationResult<IReadOnlyList<SettingDefinitionDto>>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single setting definition by its unique identifier,
    /// including options and allowed levels.
    /// </summary>
    /// <param name="id">The unique identifier of the definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the definition, or NotFound.</returns>
    Task<OperationResult<SettingDefinitionDto>> GetDefinitionByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new setting definition with its associated options and allowed levels.
    /// </summary>
    /// <param name="dto">The definition creation data including options and allowed level IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the created definition.</returns>
    Task<OperationResult<SettingDefinitionDto>> CreateDefinitionAsync(
        CreateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing setting definition, replacing its options and allowed levels
    /// with the values provided in the request.
    /// </summary>
    /// <param name="id">The unique identifier of the definition to update.</param>
    /// <param name="dto">The definition update data including options and allowed level IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the updated definition.</returns>
    Task<OperationResult<SettingDefinitionDto>> UpdateDefinitionAsync(
        Guid id,
        UpdateSettingDefinitionDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting definition. Cascades to associated options, values,
    /// and allowed level records via EF configuration.
    /// </summary>
    /// <param name="id">The unique identifier of the definition to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    Task<OperationResult> DeleteDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    #endregion
}
