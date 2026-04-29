namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Data transfer object for <see cref="Entities.Settings.SettingLevel"/>.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Name">The level name (e.g., "System", "Tenant", "User").</param>
/// <param name="Description">Optional description of this level.</param>
/// <param name="ParentId">Foreign key to the parent level. Null indicates the root level.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
public record SettingLevelDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder);
