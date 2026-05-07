namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for creating a new policy.
/// </summary>
/// <param name="Name">The policy name.</param>
/// <param name="Description">The policy's description.</param>
public record CreatePolicyDto(
    string Name,
    string? Description);
