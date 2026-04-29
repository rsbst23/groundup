namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Data transfer object for <see cref="Entities.Settings.SettingOption"/>.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="SettingDefinitionId">Foreign key to the owning setting definition.</param>
/// <param name="Value">The option's programmatic value.</param>
/// <param name="Label">Display text for the option in the UI.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
/// <param name="IsDefault">Whether this option is pre-selected by default.</param>
/// <param name="ParentOptionValue">Optional parent option value for cascading options.</param>
public record SettingOptionDto(
    Guid Id,
    Guid SettingDefinitionId,
    string Value,
    string Label,
    int DisplayOrder,
    bool IsDefault,
    string? ParentOptionValue);
