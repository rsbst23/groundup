namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A policy of the current tenant in a list (GU-96).
/// </summary>
/// <param name="Id">The policy id.</param>
/// <param name="Name">The policy name, unique in the tenant ignoring case.</param>
/// <param name="Description">An optional description.</param>
/// <param name="PermissionKeys">The keys the policy grants, sorted.</param>
/// <param name="RoleCount">How many roles of the tenant use the policy.</param>
public sealed record TenantPolicyDto(
    Guid Id,
    string Name,
    string? Description,
    List<string> PermissionKeys,
    int RoleCount);