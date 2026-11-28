using GroundUp.Auth.Core;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// The escalation guard (GU-96): a caller may only grant permissions they hold themselves, whether
/// by adding them to a policy, attaching a policy to a role, or granting a role to someone. A
/// SuperAdmin and a TenantAdmin of the current tenant hold everything, so they always pass.
/// "Holds" is measured in the current tenant.
/// </summary>
internal sealed class PermissionHoldGuard
{
    private readonly IPermissionService _permissionService;

    public PermissionHoldGuard(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    /// <summary>
    /// Succeeds when <paramref name="callerUserId"/> holds every key in <paramref name="keys"/> in the
    /// current tenant.
    /// </summary>
    /// <param name="callerUserId">Who is granting.</param>
    /// <param name="keys">The permission keys being granted (duplicates are fine).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or 403 <c>PERMISSION_NOT_HELD</c> with the missing keys, sorted, in <c>errors</c>.</returns>
    public async Task<OperationResult> CheckHoldsAsync(
        Guid callerUserId,
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return OperationResult.Ok();
        }

        var summary = await _permissionService.GetPermissionSummaryAsync(callerUserId, cancellationToken);
        if (summary.AllPermissions)
        {
            return OperationResult.Ok();
        }

        var held = summary.Permissions.ToHashSet(StringComparer.Ordinal);
        var missing = wanted.Where(k => !held.Contains(k)).Order(StringComparer.Ordinal).ToList();
        return missing.Count == 0
            ? OperationResult.Ok()
            : OperationResult.Fail(
                "You can only grant permissions you hold yourself.", 403, AuthErrorCodes.PermissionNotHeld, missing);
    }
}
