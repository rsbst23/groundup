namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for updating an existing role.
/// </summary>
/// <param name="Name">The role name.</param>
/// <param name="Description">The role's description.</param>
public record UpdateRoleDto(
    string Name,
    string? Description);
