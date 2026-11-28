namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for renaming a policy or changing its description (GU-96).
/// </summary>
/// <param name="Name">The policy name.</param>
/// <param name="Description">The policy's description.</param>
public record UpdatePolicyDto(
    string Name,
    string? Description);