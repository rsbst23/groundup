namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Request DTO for creating a setting option within a definition.
/// </summary>
/// <param name="Value">The option value stored in the database.</param>
/// <param name="Label">Display label for UI rendering.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
/// <param name="IsDefault">Whether this option is the default selection.</param>
/// <param name="ParentOptionValue">
/// For cascading selects, the parent option value this option depends on.
/// </param>
public record CreateSettingOptionDto(
    string Value,
    string Label,
    int DisplayOrder,
    bool IsDefault,
    string? ParentOptionValue);
