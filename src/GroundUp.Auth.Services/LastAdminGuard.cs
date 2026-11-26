using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services;

/// <summary>
/// Guards against removing the last active TenantAdmin from a tenant, and the last active SuperAdmin.
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
                "Unable to verify TenantAdmin count. Removal blocked for safety.", 409, AuthErrorCodes.LastAdmin);
        }

        var adminUserIds = activeAdminsResult.Data
            .Select(holder => holder.UserId)
            .ToHashSet();

        // Removing the role from someone who does not actively hold it leaves the count unchanged
        if (!adminUserIds.Contains(userId))
        {
            return OperationResult<bool>.Ok(true);
        }

        if (adminUserIds.Count <= 1)
        {
            return OperationResult<bool>.Fail(
                "Cannot remove the last TenantAdmin from the tenant. At least one TenantAdmin must remain.", 409, AuthErrorCodes.LastAdmin);
        }

        return OperationResult<bool>.Ok(true);
    }

    /// <summary>
    /// Validates whether the SuperAdmin role can be removed from the specified user. Rejects removal
    /// if the user is the last active SuperAdmin, including a SuperAdmin removing it from themself.
    /// </summary>
    /// <param name="userId">The user losing the role.</param>
    /// <param name="superAdminRoleId">The SuperAdmin role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A successful result with <c>true</c> if removal is permitted, or a 409 conflict result with error
    /// code <c>LAST_SUPERADMIN</c> if this is the only active SuperAdmin (or the count cannot be determined).
    /// </returns>
    public async Task<OperationResult<bool>> CanRemoveSuperAdminAsync(
        Guid userId,
        Guid superAdminRoleId,
        CancellationToken cancellationToken = default)
    {
        var holdersResult = await _userRoleRepository.GetActiveHolderIdsAsync(superAdminRoleId, cancellationToken);
        if (!holdersResult.Success || holdersResult.Data is null)
        {
            return OperationResult<bool>.Fail(
                "Unable to verify SuperAdmin count. Removal blocked for safety.", 409, AuthErrorCodes.LastSuperAdmin);
        }

        if (!holdersResult.Data.Contains(userId))
        {
            return OperationResult<bool>.Ok(true);
        }

        if (holdersResult.Data.Count <= 1)
        {
            return OperationResult<bool>.Fail(
                "Cannot remove the last SuperAdmin. Grant SuperAdmin to another user first.", 409, AuthErrorCodes.LastSuperAdmin);
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
                "Unable to verify TenantAdmin count. Leaving the tenant is blocked for safety.", 409, AuthErrorCodes.LastAdmin);
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
                "You are the only TenantAdmin of this organization. Assign another TenantAdmin before leaving.", 409, AuthErrorCodes.LastAdmin);
        }

        return OperationResult<bool>.Ok(true);
    }
}
