using GroundUp.Core.Enums;

namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Data transfer object for <see cref="Entities.Settings.SettingDefinition"/>.
/// Contains all definition properties except navigation collections and audit fields.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="Key">Programmatic identifier (e.g., "MaxUploadSizeMB").</param>
/// <param name="DataType">Data type that determines how the string value is deserialized.</param>
/// <param name="DefaultValue">Serialized default value.</param>
/// <param name="GroupId">Optional foreign key to the owning group.</param>
/// <param name="DisplayName">Display name for UI rendering.</param>
/// <param name="Description">Optional description shown in the UI.</param>
/// <param name="Placeholder">Placeholder text shown in empty input fields.</param>
/// <param name="Category">Optional category for additional grouping in the UI.</param>
/// <param name="DisplayOrder">Display ordering for UI rendering.</param>
/// <param name="IsVisible">Whether the setting is visible in the UI.</param>
/// <param name="IsReadOnly">Whether the setting is read-only in the UI.</param>
/// <param name="AllowMultiple">Whether the setting supports multiple values.</param>
/// <param name="IsEncrypted">Whether the value is encrypted at rest.</param>
/// <param name="IsSecret">Whether the value is masked in API responses.</param>
/// <param name="IsRequired">Whether a value is required.</param>
/// <param name="MinValue">Minimum allowed value (string representation).</param>
/// <param name="MaxValue">Maximum allowed value (string representation).</param>
/// <param name="MinLength">Minimum string length.</param>
/// <param name="MaxLength">Maximum string length.</param>
/// <param name="RegexPattern">Regular expression pattern for validation.</param>
/// <param name="ValidationMessage">Custom validation message.</param>
/// <param name="DependsOnKey">Key of the setting this definition depends on.</param>
/// <param name="DependsOnOperator">Comparison operator for the dependency.</param>
/// <param name="DependsOnValue">Value to compare against for the dependency.</param>
/// <param name="CustomValidatorType">Fully qualified type name for custom validation logic.</param>
public record SettingDefinitionDto(
    Guid Id,
    string Key,
    SettingDataType DataType,
    string? DefaultValue,
    Guid? GroupId,
    string DisplayName,
    string? Description,
    string? Placeholder,
    string? Category,
    int DisplayOrder,
    bool IsVisible,
    bool IsReadOnly,
    bool AllowMultiple,
    bool IsEncrypted,
    bool IsSecret,
    bool IsRequired,
    string? MinValue,
    string? MaxValue,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern,
    string? ValidationMessage,
    string? DependsOnKey,
    string? DependsOnOperator,
    string? DependsOnValue,
    string? CustomValidatorType);
