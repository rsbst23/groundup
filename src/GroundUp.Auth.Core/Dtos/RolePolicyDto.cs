namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a role-policy assignment for API responses and event payloads.
/// </summary>
/// <param name="Id">The unique identifier of the role-policy record.</param>
/// <param name="RoleId">The role identifier.</param>
/// <param name="PolicyId">The policy identifier.</param>
public record RolePolicyDto(
    Guid Id,
    Guid RoleId,
    Guid PolicyId);
