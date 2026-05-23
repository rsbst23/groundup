namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to create a new identity provider realm.
/// </summary>
/// <param name="RealmName">The unique realm identifier to create.</param>
/// <param name="DisplayName">Optional human-readable display name.</param>
public record CreateRealmRequest(string RealmName, string? DisplayName);
