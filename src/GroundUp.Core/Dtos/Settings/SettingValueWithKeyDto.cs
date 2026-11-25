namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// A stored setting value together with the key of its definition.
/// </summary>
/// <param name="Value">The stored value.</param>
/// <param name="DefinitionKey">The key of the value's setting definition.</param>
public sealed record SettingValueWithKeyDto(SettingValueDto Value, string DefinitionKey);
