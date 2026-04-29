namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// Logically groups related setting definitions into a composite object
/// (e.g., a "DatabaseConnection" group containing Host, Port, Username, Password).
/// </summary>
public sealed class SettingGroup : BaseEntity, IAuditable
{
    /// <summary>Programmatic identifier (e.g., "DatabaseConnection"). Unique.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display name for UI rendering.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional CSS class or icon name for UI rendering.</summary>
    public string? Icon { get; set; }

    /// <summary>Display ordering for UI rendering.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Navigation to all definitions in this group.</summary>
    public ICollection<SettingDefinition> Settings { get; set; } = new List<SettingDefinition>();

    /// <summary>Timestamp when the entity was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identifier of the user who created the entity.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when the entity was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Identifier of the user who last updated the entity.</summary>
    public string? UpdatedBy { get; set; }
}
