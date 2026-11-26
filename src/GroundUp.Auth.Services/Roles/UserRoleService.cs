using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Membership;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// The single writer of role assignments (GU-68 Requirement 3.1). Implements the public, rule-checked
/// <see cref="IUserRoleService"/> and the internal system path <see cref="IRoleGrantWriter"/>; both
/// share the save, event and cache steps.
/// </summary>
internal sealed class UserRoleService : IUserRoleService, IRoleGrantWriter
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly RoleGrantPolicy _policy;
    private readonly LastAdminGuard _lastAdminGuard;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly ILogger<UserRoleService> _logger;

    public UserRoleService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IUserRoleRepository userRoleRepository,
        IUserTenantRepository userTenantRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        RoleGrantPolicy policy,
        LastAdminGuard lastAdminGuard,
        TenantAccessRevoker accessRevoker,
        ILogger<UserRoleService> logger)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _userRoleRepository = userRoleRepository;
        _userTenantRepository = userTenantRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _policy = policy;
        _lastAdminGuard = lastAdminGuard;
        _accessRevoker = accessRevoker;
        _logger = logger;
    }

    // ── IUserRoleService (rule-checked, current user, current tenant) ──────────────

    /// <inheritdoc />
    public async Task<OperationResult<UserRoleDto>> AssignRoleAsync(
        Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var callerId = _currentUser.UserId;
        if (callerId == Guid.Empty)
        {
            return OperationResult<UserRoleDto>.Unauthorized();
        }

        var roleResult = await FindRoleForCurrentTenantAsync(roleId, cancellationToken);
        if (!roleResult.Success)
        {
            return Fail<UserRoleDto, RoleDto>(roleResult);
        }

        var role = roleResult.Data!;
        var check = await _policy.CheckAsync(callerId, userId, role, RoleChange.Assign, RoleAssignmentSource.Manual, cancellationToken);
        if (!check.Success)
        {
            return Fail<UserRoleDto>(check);
        }

        var grant = await GrantInternalAsync(userId, role, RoleAssignmentSource.Manual, cancellationToken);
        if (!grant.Success)
        {
            return Fail<UserRoleDto, RoleGrantResult>(grant);
        }

        await PublishGrantedAsync(grant.Data!, cancellationToken);
        return OperationResult<UserRoleDto>.Ok(grant.Data!.Assignment, statusCode: grant.Data.Created ? 201 : 200);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveRoleAsync(
        Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var callerId = _currentUser.UserId;
        if (callerId == Guid.Empty)
        {
            return OperationResult.Unauthorized();
        }

        var roleResult = await FindRoleForCurrentTenantAsync(roleId, cancellationToken);
        if (!roleResult.Success)
        {
            return OperationResult.Fail(roleResult.Message, roleResult.StatusCode, roleResult.ErrorCode);
        }

        var role = roleResult.Data!;
        var check = await _policy.CheckAsync(callerId, userId, role, RoleChange.Remove, RoleAssignmentSource.Manual, cancellationToken);
        if (!check.Success)
        {
            return check;
        }

        var guard = RoleKinds.Classify(role) switch
        {
            RoleKind.SuperAdmin => await _lastAdminGuard.CanRemoveSuperAdminAsync(userId, role.Id, cancellationToken),
            RoleKind.TenantAdmin => await _lastAdminGuard.CanRemoveTenantAdminAsync(userId, role.TenantId, cancellationToken),
            _ => OperationResult<bool>.Ok(true)
        };
        if (!guard.Success)
        {
            return OperationResult.Fail(guard.Message, guard.StatusCode, guard.ErrorCode);
        }

        // An admin removing a role removes it whatever the source. (GU-69: except IdP-group grants.)
        var removed = await _userRoleRepository.RemoveAssignmentsAsync(userId, role.Id, role.TenantId, source: null, cancellationToken);
        if (!removed.Success)
        {
            return OperationResult.Fail(removed.Message, removed.StatusCode, removed.ErrorCode);
        }

        if (removed.Data!.Count == 0)
        {
            return OperationResult.Ok();
        }

        await AfterRemovalAsync(userId, role.TenantId, removed.Data, cancellationToken);
        _logger.LogInformation(
            "Role {RoleId} removed from user {UserId} in tenant {TenantId} by {CallerId}",
            role.Id, userId, role.TenantId, callerId);
        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> CanGrantRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var callerId = _currentUser.UserId;
        if (callerId == Guid.Empty)
        {
            return OperationResult<bool>.Unauthorized();
        }

        var roleResult = await FindRoleForCurrentTenantAsync(roleId, cancellationToken);
        if (!roleResult.Success)
        {
            return Fail<bool, RoleDto>(roleResult);
        }

        var check = await _policy.CheckAsync(callerId, Guid.Empty, roleResult.Data!, RoleChange.Assign, RoleAssignmentSource.Manual, cancellationToken);
        return OperationResult<bool>.Ok(check.Success);
    }

    // ── IRoleGrantWriter (system path) ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<RoleGrantResult>> GrantAsync(
        Guid userId,
        Guid roleId,
        Guid tenantId,
        RoleAssignmentSource source,
        bool deferEvents = false,
        CancellationToken cancellationToken = default)
    {
        var roleResult = await _roleRepository.FindByIdAnyTenantAsync(roleId, cancellationToken);
        if (!roleResult.Success || roleResult.Data is null)
        {
            return OperationResult<RoleGrantResult>.NotFound("Role not found.");
        }

        if (roleResult.Data.TenantId != tenantId)
        {
            return OperationResult<RoleGrantResult>.NotFound("Role not found in this tenant.");
        }

        var grant = await GrantInternalAsync(userId, roleResult.Data, source, cancellationToken);
        if (grant.Success && !deferEvents)
        {
            await PublishGrantedAsync(grant.Data!, cancellationToken);
        }

        return grant;
    }

    /// <inheritdoc />
    public async Task PublishGrantedAsync(RoleGrantResult grant, CancellationToken cancellationToken = default)
    {
        if (!grant.Created)
        {
            return;
        }

        var assignment = grant.Assignment;
        _accessRevoker.EvictPermissions(assignment.UserId, assignment.TenantId);
        await _accessRevoker.PublishSafelyAsync(new EntityCreatedEvent<UserRoleDto>
        {
            Entity = assignment,
            TenantId = assignment.TenantId,
            UserId = assignment.UserId
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveBySourceAsync(
        Guid userId,
        Guid tenantId,
        RoleAssignmentSource source,
        CancellationToken cancellationToken = default)
    {
        var removed = await _userRoleRepository.RemoveAssignmentsAsync(userId, roleId: null, tenantId, source, cancellationToken);
        if (!removed.Success)
        {
            return OperationResult.Fail(removed.Message, removed.StatusCode, removed.ErrorCode);
        }

        if (removed.Data!.Count > 0)
        {
            await AfterRemovalAsync(userId, tenantId, removed.Data, cancellationToken);
        }

        return OperationResult.Ok();
    }

    // ── Shared steps ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The role for a rule-checked call: SuperAdmin from the System tenant, any other role only from
    /// the current tenant. A role in another tenant is reported as not found.
    /// </summary>
    private async Task<OperationResult<RoleDto>> FindRoleForCurrentTenantAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var roleResult = await _roleRepository.FindByIdAnyTenantAsync(roleId, cancellationToken);
        if (!roleResult.Success || roleResult.Data is null)
        {
            return OperationResult<RoleDto>.NotFound("Role not found.");
        }

        var role = roleResult.Data;
        if (RoleKinds.Classify(role) == RoleKind.SuperAdmin)
        {
            return OperationResult<RoleDto>.Ok(role);
        }

        if (_tenantContext.TenantId == Guid.Empty)
        {
            return OperationResult<RoleDto>.Fail("A tenant must be selected to change roles.", 403, ErrorCodes.TenantRequired);
        }

        return role.TenantId == _tenantContext.TenantId
            ? OperationResult<RoleDto>.Ok(role)
            : OperationResult<RoleDto>.NotFound("Role not found.");
    }

    /// <summary>
    /// Saves the assignment after checking the membership. No rules, no events.
    /// </summary>
    private async Task<OperationResult<RoleGrantResult>> GrantInternalAsync(
        Guid userId, RoleDto role, RoleAssignmentSource source, CancellationToken cancellationToken)
    {
        var membership = await EnsureMembershipAsync(userId, role, cancellationToken);
        if (!membership.Success)
        {
            return OperationResult<RoleGrantResult>.Fail(membership.Message, membership.StatusCode, membership.ErrorCode);
        }

        var existing = await FindAssignmentAsync(userId, role, source, cancellationToken);
        if (existing is not null)
        {
            return OperationResult<RoleGrantResult>.Ok(new RoleGrantResult(existing, Created: false));
        }

        var added = await _userRoleRepository.AddForTenantAsync(
            new UserRoleDto(Guid.Empty, userId, role.Id, role.TenantId, role.Name, source), cancellationToken);
        if (added.Success && added.Data is not null)
        {
            return OperationResult<RoleGrantResult>.Ok(new RoleGrantResult(added.Data, Created: true));
        }

        // A concurrent grant of the same role and source won the insert: report it as already there
        existing = await FindAssignmentAsync(userId, role, source, cancellationToken);
        return existing is not null
            ? OperationResult<RoleGrantResult>.Ok(new RoleGrantResult(existing, Created: false))
            : OperationResult<RoleGrantResult>.Fail(added.Message, added.StatusCode, added.ErrorCode);
    }

    private async Task<UserRoleDto?> FindAssignmentAsync(
        Guid userId, RoleDto role, RoleAssignmentSource source, CancellationToken cancellationToken)
    {
        var rows = await _userRoleRepository.GetAssignmentsAsync(userId, role.Id, role.TenantId, cancellationToken);
        return rows.Success ? rows.Data?.FirstOrDefault(r => r.Source == source) : null;
    }

    /// <summary>
    /// The user must be an Active or Pending member of the role's tenant (Requirement 3.3). Nobody
    /// joins the System tenant, so granting SuperAdmin creates the Active System-tenant membership if
    /// it is missing, as the SuperAdmin bootstrap does.
    /// </summary>
    private async Task<OperationResult> EnsureMembershipAsync(Guid userId, RoleDto role, CancellationToken cancellationToken)
    {
        var memberships = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, cancellationToken);
        var membership = memberships.Success
            ? memberships.Data?.FirstOrDefault(m => m.TenantId == role.TenantId)
            : null;

        if (membership is { Status: MembershipStatus.Active or MembershipStatus.Pending })
        {
            return OperationResult.Ok();
        }

        if (RoleKinds.Classify(role) != RoleKind.SuperAdmin)
        {
            return OperationResult.NotFound("The user is not a member of this organization.");
        }

        if (membership is not null)
        {
            // An inactive System-tenant membership: bring it back
            return await _userTenantRepository.ReactivateMembershipAsync(userId, role.TenantId, cancellationToken);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (!user.Success || user.Data is null)
        {
            return OperationResult.NotFound("User not found.");
        }

        var added = await _userTenantRepository.AddForTenantAsync(
            new UserTenantDto(Guid.Empty, userId, role.TenantId, user.Data.ExternalUserId, MembershipStatus.Active),
            cancellationToken);

        // 409: a concurrent grant created it first, which is the outcome we want
        return added.Success || added.StatusCode == 409
            ? OperationResult.Ok()
            : OperationResult.Fail(added.Message, added.StatusCode, added.ErrorCode);
    }

    /// <summary>
    /// After roles were removed: one role-removed event per row, then revocation and cache eviction,
    /// because the user's tokens carry the old roles.
    /// </summary>
    private Task AfterRemovalAsync(
        Guid userId, Guid tenantId, IReadOnlyList<UserRoleDto> removed, CancellationToken cancellationToken)
        => _accessRevoker.EndAccessAsync(userId, tenantId, removed, cancellationToken);
    private static OperationResult<T> Fail<T>(OperationResult source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    private static OperationResult<T> Fail<T, TSource>(OperationResult<TSource> source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);
}
