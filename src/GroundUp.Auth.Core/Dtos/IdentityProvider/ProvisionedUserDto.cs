namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Result of provisioning a user in the identity provider.
/// </summary>
/// <param name="ExternalUserId">The identity provider's unique identifier for the user.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name, if set.</param>
/// <param name="RequiresPasswordReset">Whether the user must change password on first login.</param>
public record ProvisionedUserDto(
    string ExternalUserId,
    string Email,
    string? DisplayName,
    bool RequiresPasswordReset);
