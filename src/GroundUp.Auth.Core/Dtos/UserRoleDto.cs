namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a user-role assignment for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the user-role record.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="RoleId">The role identifier.</param>
/// <param name="TenantId">The tenant identifier scoping this role assignment.</param>
public record UserRoleDto(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    Guid TenantId);
