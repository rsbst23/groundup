namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// Junction entity declaring which cascade levels a setting definition
/// can be overridden at. Unique on
/// (<see cref="SettingDefinitionId"/>, <see cref="SettingLevelId"/>).
/// </summary>
public sealed class SettingDefinitionLevel : BaseEntity
{
    /// <summary>Foreign key to the <see cref="Settings.SettingDefinition"/>.</summary>
    public Guid SettingDefinitionId { get; set; }

    /// <summary>Navigation to the setting definition.</summary>
    public SettingDefinition SettingDefinition { get; set; } = null!;

    /// <summary>Foreign key to the <see cref="Settings.SettingLevel"/>.</summary>
    public Guid SettingLevelId { get; set; }

    /// <summary>Navigation to the setting level.</summary>
    public SettingLevel SettingLevel { get; set; } = null!;
}
