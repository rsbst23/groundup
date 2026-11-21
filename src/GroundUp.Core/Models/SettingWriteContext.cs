namespace GroundUp.Core.Models;

/// <summary>
/// Describes a setting value that is about to be written. Passed to every registered
/// <see cref="Abstractions.ISettingWriteValidator"/> after the built-in checks pass.
/// </summary>
/// <param name="Key">The setting key.</param>
/// <param name="Value">The new value as submitted (may be empty when clearing a value).</param>
/// <param name="LevelId">The level the value is written at.</param>
/// <param name="LevelName">The name of that level (for example "System" or "Tenant").</param>
/// <param name="ScopeId">The scope the value applies to (tenant ID, user ID, or null for System).</param>
public sealed record SettingWriteContext(
    string Key,
    string Value,
    Guid LevelId,
    string? LevelName,
    Guid? ScopeId);
