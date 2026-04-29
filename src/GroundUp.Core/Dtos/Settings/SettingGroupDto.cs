namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Data transfer object for <see cref="Entities.Settings.SettingGroup"/>.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Key">Programmatic identifier (e.g., "DatabaseConnection").</param>
/// <param name="DisplayName">Display name for UI rendering.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Icon">Optional CSS class or icon name for UI rendering.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
public record SettingGroupDto(
    Guid Id,
    string Key,
    string DisplayName,
    string? Description,
    string? Icon,
    int DisplayOrder);
