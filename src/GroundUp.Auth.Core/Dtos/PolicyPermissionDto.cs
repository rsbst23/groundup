namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a policy-permission assignment for API responses and event payloads.
/// </summary>
/// <param name="Id">The unique identifier of the policy-permission record.</param>
/// <param name="PolicyId">The policy identifier.</param>
/// <param name="PermissionId">The permission identifier.</param>
public record PolicyPermissionDto(
    Guid Id,
    Guid PolicyId,
    Guid PermissionId);
