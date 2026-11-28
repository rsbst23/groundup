using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Membership;

/// <summary>
/// Administration of the current tenant's members (GU-68 Requirement 5). Registered by
/// <c>AddGroundUpAuth</c> through the authorization proxy, so the permissions below are enforced
/// for HTTP and SDK callers alike. Role changes for members go through
/// <see cref="Roles.IUserRoleService"/>.
/// </summary>
public interface IMemberManagementService
{
    /// <summary>
    /// Lists the current tenant's Active and Pending members with their roles and each role's
    /// sources, by email unless sorted. Sort: <c>email</c>, <c>displayName</c>, <c>status</c>; filter:
    /// <c>status</c>; search: email and display name. Other fields are ignored.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.MembersRead)]
    Task<OperationResult<PaginatedData<TenantMemberDto>>> ListMembersAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The current tenant's Active and Pending members holding the role (GU-97d), with the same fields
    /// as <see cref="ListMembersAsync"/>. 404 when the role is not in the current tenant.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.MembersRead, PermissionKeysAuth.RolesRead)]
    Task<OperationResult<PaginatedData<TenantMemberDto>>> ListRoleMembersAsync(
        Guid roleId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a member from the current tenant, like leaving: the membership is deactivated, all the
    /// member's roles in the tenant are removed, and their existing tokens for the tenant stop working.
    /// The caller must also be allowed to remove each role the member holds (so only a TenantAdmin or
    /// SuperAdmin can remove a TenantAdmin), and the tenant's last active TenantAdmin cannot be removed.
    /// </summary>
    /// <returns>
    /// Success; 403 when a role the member holds may not be removed by the caller; 404 when the user
    /// is not an Active or Pending member; 409 <c>LAST_ADMIN</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.MembersManage)]
    Task<OperationResult> RemoveMemberAsync(Guid userId, CancellationToken cancellationToken = default);
}
