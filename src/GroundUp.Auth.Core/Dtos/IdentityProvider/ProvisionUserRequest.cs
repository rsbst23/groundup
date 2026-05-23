namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to provision a new user in the identity provider.
/// </summary>
/// <param name="Email">The user's email address (used as username).</param>
/// <param name="DisplayName">Optional display name for the user.</param>
/// <param name="InitialPassword">Optional initial password. If null, user must set via email.</param>
/// <param name="RequirePasswordReset">Whether the user must change password on first login.</param>
public record ProvisionUserRequest(
    string Email,
    string? DisplayName,
    string? InitialPassword,
    bool RequirePasswordReset);
