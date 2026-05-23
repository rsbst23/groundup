using GroundUp.Core.Enums;

namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Request to idempotently ensure a setting definition exists. If the definition
/// already exists (matched by <see cref="Key"/>), no changes are made.
/// Used by module seeders to register their required settings without overwriting
/// admin-modified definitions.
/// </summary>
/// <param name="Key">Programmatic identifier (e.g., "auth.keycloak.shared-realm-name").</param>
/// <param name="DataType">Data type for value deserialization.</param>
/// <param name="DefaultValue">Serialized default value.</param>
/// <param name="DisplayName">Display name for UI rendering.</param>
/// <param name="Description">Optional description shown in the UI.</param>
/// <param name="Category">Optional category for additional grouping.</param>
/// <param name="GroupKey">Key of the group this definition belongs to.</param>
/// <param name="GroupDisplayName">Display name for the group (created if not exists).</param>
/// <param name="AllowedLevelNames">Names of cascade levels where this setting can be overridden.</param>
/// <param name="RegexPattern">Optional regex pattern for value validation.</param>
/// <param name="ValidationMessage">Optional custom validation message.</param>
/// <param name="IsRequired">Whether a value is required.</param>
/// <param name="IsSecret">Whether the value is masked in API responses.</param>
/// <param name="IsEncrypted">Whether the value is encrypted at rest.</param>
public record EnsureSettingDefinitionRequest(
    string Key,
    SettingDataType DataType,
    string DefaultValue,
    string DisplayName,
    string? Description,
    string? Category,
    string GroupKey,
    string GroupDisplayName,
    IReadOnlyList<string> AllowedLevelNames,
    string? RegexPattern = null,
    string? ValidationMessage = null,
    bool IsRequired = false,
    bool IsSecret = false,
    bool IsEncrypted = false);
