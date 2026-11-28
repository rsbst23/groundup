using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Input DTO for creating a new role.
/// </summary>
/// <param name="Name">The role name.</param>
/// <param name="Description">The role's description.</param>
/// <param name="RoleType">
/// Application (the default when omitted) or Workspace. The roles API refuses System (GU-95).
/// </param>
public record CreateRoleDto(
    string Name,
    string? Description,
    RoleType RoleType = RoleType.Application);
