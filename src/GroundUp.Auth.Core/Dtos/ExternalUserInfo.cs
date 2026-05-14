namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// User information retrieved from an external identity provider.
/// </summary>
/// <param name="ExternalUserId">The user's unique identifier in the external identity provider.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name, if available.</param>
/// <param name="Attributes">Additional user attributes from the identity provider.</param>
public record ExternalUserInfo(string ExternalUserId, string Email, string? DisplayName, IDictionary<string, string>? Attributes);
