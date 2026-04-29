namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// Represents a named tier in the cascade hierarchy (e.g., "System", "Tenant", "User").
/// Forms a self-referencing tree via <see cref="ParentId"/>.
/// The root level has a null ParentId. Resolution walks UP the parent chain.
/// </summary>
public sealed class SettingLevel : BaseEntity, IAuditable
{
    /// <summary>The level name (e.g., "System", "Tenant", "User").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of this level.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Foreign key to the parent level. Null indicates the root level.
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>Navigation to the parent level.</summary>
    public SettingLevel? Parent { get; set; }

    /// <summary>Navigation to child levels.</summary>
    public ICollection<SettingLevel> Children { get; set; } = new List<SettingLevel>();

    /// <summary>Display ordering for UI rendering.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Timestamp when the entity was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identifier of the user who created the entity.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when the entity was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Identifier of the user who last updated the entity.</summary>
    public string? UpdatedBy { get; set; }
}
