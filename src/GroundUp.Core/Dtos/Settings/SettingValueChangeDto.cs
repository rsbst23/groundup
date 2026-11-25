namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Result of writing a setting value: the saved value and the stored value it replaced.
/// </summary>
/// <param name="Value">The saved value.</param>
/// <param name="OldValue">The stored value before the write (as stored, for example encrypted); null for a new value.</param>
public sealed record SettingValueChangeDto(SettingValueDto Value, string? OldValue);
