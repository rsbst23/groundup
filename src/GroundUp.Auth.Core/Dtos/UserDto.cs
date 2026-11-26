namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a user entity for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the user.</param>
/// <param name="ExternalUserId">
/// The user's primary external identity provider identifier. Null for a pending user who has not signed in yet.
/// </param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="IsActive">Whether the user account is active.</param>
/// <param name="MergedIntoUserId">Set when this pending user was merged into another user.</param>
public record UserDto(
    Guid Id,
    string? ExternalUserId,
    string Email,
    string DisplayName,
    bool IsActive,
    Guid? MergedIntoUserId = null);
