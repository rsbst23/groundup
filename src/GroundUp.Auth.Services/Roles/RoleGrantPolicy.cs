using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Decides whether a user may grant or remove a role (GU-68 Requirement 3.2).
/// <para>
/// The SuperAdmin and TenantAdmin rules read the caller's roles directly, not through
/// <see cref="IPermissionService"/>: a TenantAdmin passes every permission check in their tenant, so
/// a permission-based rule could not keep SuperAdmin away from TenantAdmins.
/// </para>
/// <para>
/// A holder of <c>auth.roles.assign</c> can only grant a role whose permissions they all hold
/// (GU-96, <see cref="PermissionHoldGuard"/>); otherwise 403 <c>PERMISSION_NOT_HELD</c>.
/// </para>
/// </summary>
internal sealed class RoleGrantPolicy
{
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionService _permissionService;
    private readonly PermissionHoldGuard _holdGuard;
    private readonly ITenantContext _tenantContext;
    private readonly IEnumerable<IRoleAssignmentRule> _rules;

    public RoleGrantPolicy(
        IUserRoleRepository userRoleRepository,
        IRoleRepository roleRepository,
        IPermissionService permissionService,
        ITenantContext tenantContext,
        IEnumerable<IRoleAssignmentRule> rules)
    {
        _userRoleRepository = userRoleRepository;
        _roleRepository = roleRepository;
        _permissionService = permissionService;
        _holdGuard = new PermissionHoldGuard(permissionService);
        _tenantContext = tenantContext;
        _rules = rules;
    }

    /// <summary>
    /// Checks the built-in rules, then every registered <see cref="IRoleAssignmentRule"/>.
    /// </summary>
    /// <param name="callerUserId">Who is making the change.</param>
    /// <param name="targetUserId">Who receives or loses the role (<see cref="Guid.Empty"/> when not known yet).</param>
    /// <param name="role">The role.</param>
    /// <param name="change">Assign or remove.</param>
    /// <param name="source">How the role is being granted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or 403 <c>FORBIDDEN</c> with the reason.</returns>
    public async Task<OperationResult> CheckAsync(
        Guid callerUserId,
        Guid targetUserId,
        RoleDto role,
        RoleChange change,
        RoleAssignmentSource source,
        CancellationToken cancellationToken = default)
    {
        if (callerUserId == Guid.Empty)
        {
            return OperationResult.Forbidden("Only a signed-in user can change roles.");
        }

        var kind = RoleKinds.Classify(role);
        var isSuperAdmin = await IsSuperAdminAsync(callerUserId, cancellationToken);

        bool allowed;
        var throughAssignPermission = false;
        switch (kind)
        {
            case RoleKind.SuperAdmin:
                allowed = isSuperAdmin;
                break;
            case RoleKind.TenantAdmin:
                allowed = isSuperAdmin || await IsTenantAdminAsync(callerUserId, role.TenantId, cancellationToken);
                break;
            default:
                if (isSuperAdmin || await IsTenantAdminAsync(callerUserId, role.TenantId, cancellationToken))
                {
                    allowed = true;
                }
                else
                {
                    allowed = throughAssignPermission = await HasAssignPermissionAsync(callerUserId, role.TenantId, cancellationToken);
                }

                break;
        }

        if (!allowed)
        {
            return OperationResult.Forbidden(kind switch
            {
                RoleKind.SuperAdmin => "Only a SuperAdmin can grant or remove the SuperAdmin role.",
                RoleKind.TenantAdmin => "Only a TenantAdmin of this organization or a SuperAdmin can grant or remove the TenantAdmin role.",
                _ => "You do not have permission to grant or remove this role."
            });
        }

        // GU-96: a holder of auth.roles.assign can only grant a role whose permissions they all hold.
        // Admins hold everything; removing a role grants nothing.
        if (throughAssignPermission && change == RoleChange.Assign)
        {
            var held = await CheckCallerHoldsRolePermissionsAsync(callerUserId, role, cancellationToken);
            if (!held.Success)
            {
                return held;
            }
        }

        var context = new RoleAssignmentContext(callerUserId, targetUserId, role, role.TenantId, change, source);
        foreach (var rule in _rules)
        {
            var ruleResult = await rule.EvaluateAsync(context, cancellationToken);
            if (!ruleResult.Success)
            {
                return ruleResult;
            }
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// The current tenant is the role's tenant here (<see cref="HasAssignPermissionAsync"/> requires it),
    /// so the caller's permissions are measured where the role applies. If the role's permissions
    /// cannot be read the grant is refused.
    /// </summary>
    private async Task<OperationResult> CheckCallerHoldsRolePermissionsAsync(Guid callerUserId, RoleDto role, CancellationToken cancellationToken)
    {
        var grants = await _roleRepository.GetPermissionGrantsAsync(role.Id, cancellationToken);
        if (grants is not { Success: true, Data: not null })
        {
            return OperationResult.Forbidden($"The permissions of the role '{role.Name}' could not be checked.");
        }

        var held = await _holdGuard.CheckHoldsAsync(callerUserId, grants.Data.Select(g => g.PermissionKey), cancellationToken);
        return held.Success
            ? held
            : OperationResult.Fail(
                $"You cannot grant the role '{role.Name}' because it has permissions you do not hold.",
                held.StatusCode, held.ErrorCode, held.Errors);
    }

    private async Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await _userRoleRepository.GetSystemRolesForUserAsync(userId, cancellationToken);
        return roles.Success && roles.Data is not null
            && roles.Data.Any(r => string.Equals(r.RoleName, AuthRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsTenantAdminAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var roles = await _userRoleRepository.GetByUserIdForTenantAsync(userId, tenantId, cancellationToken);
        return roles.Success && roles.Data is not null
            && roles.Data.Any(r => string.Equals(r.RoleName, AuthRoleNames.TenantAdmin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The permission service resolves permissions in the current tenant context, so the permission
    /// only counts when that context is the role's tenant. Callers acting for another tenant (sign-in
    /// callbacks) point the context at it first.
    /// </summary>
    private async Task<bool> HasAssignPermissionAsync(Guid userId, Guid roleTenantId, CancellationToken cancellationToken)
        => _tenantContext.TenantId == roleTenantId
           && await _permissionService.HasPermissionAsync(userId, PermissionKeysAuth.RolesAssign, cancellationToken);
}
