using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a role entity for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the role.</param>
/// <param name="Name">The role name.</param>
/// <param name="Description">The role's description.</param>
/// <param name="RoleType">The category of the role (System, Application, or Workspace).</param>
/// <param name="TenantId">The tenant that owns this role.</param>
/// <param name="IsSystem">Whether the role is framework-defined and immutable.</param>
public record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    RoleType RoleType,
    Guid TenantId,
    bool IsSystem);
