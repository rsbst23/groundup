using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A role of the current tenant as the roles API returns it (GU-95): the fields of
/// <see cref="RoleDto"/> plus whether it is the tenant's default role for new members.
/// </summary>
/// <param name="Id">The role id.</param>
/// <param name="Name">The role name, unique in the tenant ignoring case.</param>
/// <param name="Description">An optional description.</param>
/// <param name="RoleType">Application or Workspace (System only for the SuperAdmin role, which is not listed).</param>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="IsSystem">True for a built-in role (TenantAdmin), which cannot be renamed or deleted.</param>
/// <param name="IsDefault">True when new members of the tenant get this role.</param>
/// <param name="PolicyCount">How many policies are attached to the role (GU-97); 0 for TenantAdmin, which needs none.</param>
public sealed record TenantRoleDto(
    Guid Id,
    string Name,
    string? Description,
    RoleType RoleType,
    Guid TenantId,
    bool IsSystem,
    bool IsDefault,
    int PolicyCount = 0)
{
    /// <summary>
    /// Builds the DTO from a <see cref="RoleDto"/>.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="isDefault">Whether it is the tenant's default role.</param>
    /// <param name="policyCount">How many policies are attached to the role.</param>
    public static TenantRoleDto From(RoleDto role, bool isDefault, int policyCount = 0)
        => new(role.Id, role.Name, role.Description, role.RoleType, role.TenantId, role.IsSystem, isDefault, policyCount);
}
