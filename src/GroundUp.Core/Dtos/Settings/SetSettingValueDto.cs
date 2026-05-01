namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Request DTO for setting a value at a specific level and scope.
/// Used by the <c>PUT /api/settings/{key}</c> endpoint.
/// </summary>
/// <param name="Value">The serialized setting value to store.</param>
/// <param name="LevelId">The cascade level to set the value at.</param>
/// <param name="ScopeId">
/// The specific entity at the level (e.g., TenantId). Null for system level.
/// </param>
public record SetSettingValueDto(
    string Value,
    Guid LevelId,
    Guid? ScopeId);
