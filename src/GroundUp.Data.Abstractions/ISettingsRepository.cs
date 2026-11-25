using GroundUp.Core.Dtos.Settings;

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
}
