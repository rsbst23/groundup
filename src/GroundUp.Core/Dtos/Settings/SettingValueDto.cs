namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Data transfer object for <see cref="Entities.Settings.SettingValue"/>.
/// </summary>
/// <param name="Id">Unique identifier.</param>
/// <param name="SettingDefinitionId">Foreign key to the setting definition.</param>
/// <param name="LevelId">Foreign key to the setting level.</param>
/// <param name="ScopeId">The specific entity at this level (e.g., TenantId). Null for system level.</param>
/// <param name="Value">The serialized setting value.</param>
public record SettingValueDto(
    Guid Id,
    Guid SettingDefinitionId,
    Guid LevelId,
    Guid? ScopeId,
    string? Value);
