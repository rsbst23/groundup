namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Request DTO for creating a new setting group.
/// </summary>
/// <param name="Key">Programmatic identifier (e.g., "DatabaseConnection").</param>
/// <param name="DisplayName">Display name for UI rendering.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Icon">Optional CSS class or icon name for UI rendering.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
public record CreateSettingGroupDto(
    string Key,
    string DisplayName,
    string? Description,
    string? Icon,
    int DisplayOrder);
