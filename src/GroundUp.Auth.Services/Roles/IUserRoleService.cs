using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Grants and removes a user's roles in the current tenant, as the current user (GU-68). This is the
/// one public way to change role assignments; every call is checked against the grant rules:
/// <list type="bullet">
/// <item>SuperAdmin: only a SuperAdmin may grant or remove it.</item>
/// <item>TenantAdmin: only a TenantAdmin of the tenant, or a SuperAdmin.</item>
/// <item>Other roles: holders of <c>auth.roles.assign</c>; TenantAdmins and SuperAdmins always may.</item>
/// <item>Then every registered <see cref="IRoleAssignmentRule"/>.</item>
/// </list>
/// The tenant's last active TenantAdmin and the last active SuperAdmin cannot lose that role.
/// The rules are checked in code, not with <c>[RequiresPermission]</c>, because they depend on the
/// target role, so the service is registered without <c>AddAuthorized</c> and checks the same way
/// for HTTP and SDK callers.
/// </summary>
public interface IUserRoleService
{
    /// <summary>
    /// Grants <paramref name="roleId"/> to <paramref name="userId"/> (source Manual). The user must be
    /// an Active or Pending member of the role's tenant; granting SuperAdmin creates the System-tenant
    /// membership if needed. Granting a role the user already holds from this source changes nothing.
    /// </summary>
    /// <returns>
    /// The assignment; 401 when no user is signed in; 403 <c>FORBIDDEN</c> when the rules refuse;
    /// 404 when the role or membership does not exist.
    /// </returns>
    Task<OperationResult<UserRoleDto>> AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes <paramref name="roleId"/> from <paramref name="userId"/>, whatever the source. Records a
    /// token revocation cutoff, because roles are part of issued tokens. Removing a role the user does
    /// not hold changes nothing.
    /// </summary>
    /// <returns>
    /// Success; 401 when no user is signed in; 403 <c>FORBIDDEN</c> when the rules refuse; 404 when the
    /// role does not exist; 409 <c>LAST_ADMIN</c> or <c>LAST_SUPERADMIN</c> for the last holder.
    /// </returns>
    Task<OperationResult> RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the current user may grant <paramref name="roleId"/> in the current tenant, for
    /// example to decide which roles an admin screen offers.
    /// </summary>
    Task<OperationResult<bool>> CanGrantRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}
