using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Guards against removing the last active TenantAdmin from a tenant.
/// Before a TenantAdmin role assignment is removed, this guard checks how many
/// active users hold TenantAdmin in the tenant. If exactly one remains, removal is rejected.
/// </summary>
public sealed class LastAdminGuard
{
    private readonly IUserRoleRepository _userRoleRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="LastAdminGuard"/>.
    /// </summary>
    /// <param name="userRoleRepository">Repository for querying role assignments.</param>
    public LastAdminGuard(IUserRoleRepository userRoleRepository)
    {
        _userRoleRepository = userRoleRepository;
    }

    /// <summary>
    /// Validates whether a TenantAdmin role assignment can be removed from the specified user
    /// in the given tenant. Rejects removal if the user is the last active TenantAdmin.
    /// </summary>
    /// <param name="userId">The user whose TenantAdmin assignment is being removed.</param>
    /// <param name="tenantId">The tenant from which the TenantAdmin is being removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A successful result with <c>true</c> if removal is permitted (2+ TenantAdmins exist),
    /// or a 409 conflict result with error code "LAST_ADMIN" if this is the sole TenantAdmin.
    /// </returns>
    public async Task<OperationResult<bool>> CanRemoveTenantAdminAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var activeAdminsResult = await _userRoleRepository
            .GetTenantAdminHoldersAsync(tenantId, cancellationToken);

        if (!activeAdminsResult.Success || activeAdminsResult.Data is null)
        {
            // If we can't determine the count, err on the side of safety
            return OperationResult<bool>.Fail(
                "Unable to verify TenantAdmin count. Removal blocked for safety.", 409, "LAST_ADMIN");
        }

        var activeAdminCount = activeAdminsResult.Data.Count;

        if (activeAdminCount <= 1)
        {
            return OperationResult<bool>.Fail(
                "Cannot remove the last TenantAdmin from the tenant. At least one TenantAdmin must remain.", 409, "LAST_ADMIN");
        }

        return OperationResult<bool>.Ok(true);
    }
}
