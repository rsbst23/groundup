namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to update an existing identity provider realm.
/// </summary>
/// <param name="DisplayName">New display name, or null to leave unchanged.</param>
/// <param name="Enabled">New enabled state, or null to leave unchanged.</param>
public record UpdateRealmRequest(string? DisplayName, bool? Enabled);
