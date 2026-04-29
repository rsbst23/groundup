namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// A selectable option for a setting definition of select or multi-select type.
/// Supports cascading options via <see cref="ParentOptionValue"/>.
/// </summary>
public sealed class SettingOption : BaseEntity
{
    /// <summary>Foreign key to the owning <see cref="Settings.SettingDefinition"/>.</summary>
    public Guid SettingDefinitionId { get; set; }

    /// <summary>Navigation to the owning setting definition.</summary>
    public SettingDefinition SettingDefinition { get; set; } = null!;

    /// <summary>The option's programmatic value.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Display text for the option in the UI.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Display ordering for UI rendering.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Whether this option is pre-selected by default.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Optional parent option value for cascading options that filter
    /// based on a parent setting's selected value.
    /// </summary>
    public string? ParentOptionValue { get; set; }
}
