using GroundUp.Core.Enums;

namespace GroundUp.Core.Entities.Settings;

/// <summary>
/// Declares a single setting's key, data type, default value, UI metadata,
/// validation rules, conditional dependencies, and encryption flags.
/// Stores ALL metadata needed to render a settings UI.
/// </summary>
public sealed class SettingDefinition : BaseEntity, IAuditable
{
    // ── Identity ──────────────────────────────────────────────

    /// <summary>Programmatic identifier (e.g., "MaxUploadSizeMB"). Unique.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Data type that determines how the string value is deserialized.</summary>
    public SettingDataType DataType { get; set; }

    /// <summary>Serialized default value (optional, max 4000 chars).</summary>
    public string? DefaultValue { get; set; }

    // ── Group relationship ────────────────────────────────────

    /// <summary>Optional foreign key to the owning <see cref="SettingGroup"/>.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Navigation to the owning group.</summary>
    public SettingGroup? Group { get; set; }

    // ── UI metadata ───────────────────────────────────────────

    /// <summary>Display name for UI rendering.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional description shown in the UI.</summary>
    public string? Description { get; set; }

    /// <summary>Optional category for additional grouping in the UI.</summary>
    public string? Category { get; set; }

    /// <summary>Display ordering for UI rendering.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Whether the setting is visible in the UI. Default true.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Whether the setting is read-only in the UI. Default false.</summary>
    public bool IsReadOnly { get; set; }

    // ── Multi-value ───────────────────────────────────────────

    /// <summary>Whether the setting supports multiple values stored as a JSON array.</summary>
    public bool AllowMultiple { get; set; }

    // ── Encryption ────────────────────────────────────────────

    /// <summary>Whether the value is encrypted at rest via <see cref="Abstractions.ISettingEncryptionProvider"/>.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>Whether the value is masked in API responses.</summary>
    public bool IsSecret { get; set; }

    // ── Validation ────────────────────────────────────────────

    /// <summary>Whether a value is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Minimum allowed value (string representation, type-dependent).</summary>
    public string? MinValue { get; set; }

    /// <summary>Maximum allowed value (string representation, type-dependent).</summary>
    public string? MaxValue { get; set; }

    /// <summary>Minimum string length (for String/Json types).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum string length (for String/Json types).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Regular expression pattern for value validation.</summary>
    public string? RegexPattern { get; set; }

    /// <summary>Custom validation message shown when validation fails.</summary>
    public string? ValidationMessage { get; set; }

    // ── Conditional dependencies ──────────────────────────────

    /// <summary>Key of the setting this definition depends on.</summary>
    public string? DependsOnKey { get; set; }

    /// <summary>Comparison operator for the dependency (see <see cref="Constants.SettingDependencyOperator"/>).</summary>
    public string? DependsOnOperator { get; set; }

    /// <summary>Value to compare against for the dependency.</summary>
    public string? DependsOnValue { get; set; }

    // ── Custom validation ─────────────────────────────────────

    /// <summary>Fully qualified type name for custom validation logic.</summary>
    public string? CustomValidatorType { get; set; }

    // ── Navigation collections ────────────────────────────────

    /// <summary>Selectable options for select/multi-select settings.</summary>
    public ICollection<SettingOption> Options { get; set; } = new List<SettingOption>();

    /// <summary>Stored values at various levels and scopes.</summary>
    public ICollection<SettingValue> Values { get; set; } = new List<SettingValue>();

    /// <summary>Allowed cascade levels for this definition.</summary>
    public ICollection<SettingDefinitionLevel> AllowedLevels { get; set; } = new List<SettingDefinitionLevel>();

    // ── IAuditable ────────────────────────────────────────────

    /// <summary>Timestamp when the entity was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identifier of the user who created the entity.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when the entity was last updated.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Identifier of the user who last updated the entity.</summary>
    public string? UpdatedBy { get; set; }
}
