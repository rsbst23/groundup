namespace GroundUp.Core.Dtos.Settings;

/// <summary>
/// Represents a fully resolved setting with its effective value and provenance information.
/// Used by <c>GetAllForScopeAsync</c> and <c>GetGroupAsync</c> to provide a complete
/// settings view showing where each value came from and whether it is inherited.
/// </summary>
/// <param name="Definition">The full setting definition metadata.</param>
/// <param name="EffectiveValue">
/// The resolved value after cascade resolution. Decrypted if the setting is encrypted,
/// or masked if the setting is secret (in bulk reads).
/// </param>
/// <param name="SourceLevelId">
/// The cascade level the effective value came from.
/// Null indicates the value is the definition's default.
/// </param>
/// <param name="SourceScopeId">
/// The specific scope entity the effective value came from.
/// Null when the value is from the definition default or a system-level override.
/// </param>
/// <param name="IsInherited">
/// True if the effective value was inherited from a higher level in the scope chain
/// or from the definition default. False if the value is directly set at the most
/// specific scope entry (first entry in the scope chain).
/// </param>
public record ResolvedSettingDto(
    SettingDefinitionDto Definition,
    string? EffectiveValue,
    Guid? SourceLevelId,
    Guid? SourceScopeId,
    bool IsInherited);
