namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A policy of the current tenant with its permissions and the roles using it (GU-96).
/// </summary>
/// <param name="Id">The policy id.</param>
/// <param name="Name">The policy name.</param>
/// <param name="Description">An optional description.</param>
/// <param name="Permissions">The permissions the policy grants, sorted by key.</param>
/// <param name="Roles">The roles of the tenant that use the policy, sorted by name.</param>
public sealed record PolicyDetailDto(
    Guid Id,
    string Name,
    string? Description,
    List<PermissionDto> Permissions,
    List<EntityRefDto> Roles);