namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of an identity provider realm.
/// </summary>
/// <param name="RealmName">The unique realm identifier.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Enabled">Whether the realm is active.</param>
public record RealmDto(string RealmName, string DisplayName, bool Enabled);
