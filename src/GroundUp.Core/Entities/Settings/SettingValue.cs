namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// Stores the actual value of a setting at a specific level and scope.
/// Unique on (<see cref="SettingDefinitionId"/>, <see cref="LevelId"/>, <see cref="ScopeId"/>).
/// </summary>
public sealed class SettingValue : BaseEntity, IAuditable
{
    /// <summary>Foreign key to the <see cref="Settings.SettingDefinition"/> this value belongs to.</summary>
    public Guid SettingDefinitionId { get; set; }

    /// <summary>Navigation to the setting definition.</summary>
    public SettingDefinition SettingDefinition { get; set; } = null!;

    /// <summary>Foreign key to the <see cref="SettingLevel"/> this value is set at.</summary>
    public Guid LevelId { get; set; }

    /// <summary>Navigation to the setting level.</summary>
    public SettingLevel Level { get; set; } = null!;

    /// <summary>
    /// The specific entity at this level (e.g., a TenantId or UserId).
    /// Null indicates the root/system level where no specific entity scope applies.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// The serialized setting value. Encrypted if the definition's
    /// <see cref="SettingDefinition.IsEncrypted"/> flag is true.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>Timestamp when the entity was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identifier of the user who created the entity.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when the entity was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Identifier of the user who last updated the entity.</summary>
    public string? UpdatedBy { get; set; }
}
