namespace GroundUp.Core.Models;

/// <summary>
/// Represents a single entry in the cascade scope chain.
/// The consuming application builds a list of these from most specific
/// to least specific (e.g., User → Team → Tenant → System).
/// </summary>
/// <param name="LevelId">The cascade level this entry represents.</param>
/// <param name="ScopeId">
/// The specific entity at this level (e.g., a UserId or TenantId).
/// Null indicates the root/system level where no specific entity scope applies.
/// </param>
public readonly record struct SettingScopeEntry(Guid LevelId, Guid? ScopeId);
