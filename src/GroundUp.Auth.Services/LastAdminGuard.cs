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

    /// <summary>
    /// Validates whether a user may leave a tenant without leaving it with no TenantAdmin.
    /// Members who are not TenantAdmins may always leave. A TenantAdmin may leave only if at
    /// least one other active TenantAdmin remains in the tenant.
    /// </summary>
    /// <param name="userId">The user who wants to leave.</param>
    /// <param name="tenantId">The tenant being left.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A successful result with <c>true</c> if the user may leave, or a 409 conflict result with
    /// error code "LAST_ADMIN" if the user is the only active TenantAdmin (or the admin count
    /// cannot be determined).
    /// </returns>
    public async Task<OperationResult<bool>> CanLeaveTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var activeAdminsResult = await _userRoleRepository
            .GetTenantAdminHoldersAsync(tenantId, cancellationToken);

        if (!activeAdminsResult.Success || activeAdminsResult.Data is null)
        {
            // If we can't determine the admin set, err on the side of safety
            return OperationResult<bool>.Fail(
                "Unable to verify TenantAdmin count. Leaving the tenant is blocked for safety.", 409, "LAST_ADMIN");
        }

        var adminUserIds = activeAdminsResult.Data
            .Select(holder => holder.UserId)
            .ToHashSet();

        // Regular members never affect the TenantAdmin count
        if (!adminUserIds.Contains(userId))
        {
            return OperationResult<bool>.Ok(true);
        }

        if (adminUserIds.Count <= 1)
        {
            return OperationResult<bool>.Fail(
                "You are the only TenantAdmin of this organization. Assign another TenantAdmin before leaving.", 409, "LAST_ADMIN");
        }

        return OperationResult<bool>.Ok(true);
    }
}
