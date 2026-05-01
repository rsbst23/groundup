namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Request DTO for updating an existing setting level.
/// </summary>
/// <param name="Name">The level name (e.g., "System", "Tenant", "User").</param>
/// <param name="Description">Optional description of this level.</param>
/// <param name="ParentId">Foreign key to the parent level. Null indicates the root level.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
public record UpdateSettingLevelDto(
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder);
