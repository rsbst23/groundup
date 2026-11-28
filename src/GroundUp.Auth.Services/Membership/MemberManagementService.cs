using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Membership;

/// <summary>
/// Default <see cref="IMemberManagementService"/>. Permissions are checked by the authorization
/// proxy; this class checks the per-role rules and the last-admin guard.
/// </summary>
internal sealed class MemberManagementService : IMemberManagementService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly LastAdminGuard _lastAdminGuard;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly ILogger<MemberManagementService> _logger;

    public MemberManagementService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IUserTenantRepository userTenantRepository,
        IUserRoleRepository userRoleRepository,
        IRoleRepository roleRepository,
        IInvitationRepository invitationRepository,
        RoleGrantPolicy rolePolicy,
        LastAdminGuard lastAdminGuard,
        TenantAccessRevoker accessRevoker,
        ILogger<MemberManagementService> logger)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _userTenantRepository = userTenantRepository;
        _userRoleRepository = userRoleRepository;
        _roleRepository = roleRepository;
        _invitationRepository = invitationRepository;
        _rolePolicy = rolePolicy;
        _lastAdminGuard = lastAdminGuard;
        _accessRevoker = accessRevoker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantMemberDto>>> ListMembersAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<TenantMemberDto>>.Fail(
                "A tenant must be selected to list members.", 403, ErrorCodes.TenantRequired);
        }

        return await _userTenantRepository.GetMembersAsync(tenantId, filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantMemberDto>>> ListRoleMembersAsync(
        Guid roleId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filterParams);
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<TenantMemberDto>>.Fail(
                "A tenant must be selected to list members.", 403, ErrorCodes.TenantRequired);
        }

        // Tenant-scoped read: a role of another tenant is not found; the System tenant's roles are not
        // reachable through the roles API either
        var role = tenantId == AuthRoleNames.SystemTenantId
            ? OperationResult<RoleDto>.NotFound()
            : await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (!role.Success || role.Data is null || role.Data.TenantId != tenantId)
        {
            return OperationResult<PaginatedData<TenantMemberDto>>.NotFound("Role not found.");
        }

        return await _userTenantRepository.GetRoleMembersAsync(tenantId, roleId, filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveMemberAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult.Fail("A tenant must be selected to remove members.", 403, ErrorCodes.TenantRequired);
        }

        var memberships = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, cancellationToken);
        var membership = memberships.Success
            ? memberships.Data?.FirstOrDefault(m => m.TenantId == tenantId)
            : null;
        if (membership is not { Status: MembershipStatus.Active or MembershipStatus.Pending })
        {
            return OperationResult.NotFound("The user is not a member of this organization.");
        }

        // Removing a member removes their roles, so the caller must be allowed to remove each of them.
        // Without this a holder of auth.members.manage could remove a TenantAdmin.
        var rolesCheck = await CheckCanRemoveRolesAsync(userId, tenantId, cancellationToken);
        if (!rolesCheck.Success)
        {
            return rolesCheck;
        }

        var guard = await _lastAdminGuard.CanLeaveTenantAsync(userId, tenantId, cancellationToken);
        if (!guard.Success)
        {
            return OperationResult.Fail(guard.Message, guard.StatusCode, guard.ErrorCode);
        }

        // A pending member's invitation goes first, so its link can never re-activate the membership
        if (membership.Status == MembershipStatus.Pending)
        {
            var revoked = await RevokeInvitationsAsync(userId, tenantId, cancellationToken);
            if (!revoked.Success)
            {
                return revoked;
            }
        }

        var removed = await _userTenantRepository.DeactivateMembershipAsync(userId, tenantId, cancellationToken);
        if (!removed.Success)
        {
            return OperationResult.Fail(removed.Message, removed.StatusCode, removed.ErrorCode);
        }

        await _accessRevoker.EndAccessAsync(userId, tenantId, removed.Data, cancellationToken);

        _logger.LogInformation(
            "Member {UserId} removed from tenant {TenantId} by {CallerId}",
            userId, tenantId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    private async Task<OperationResult> RevokeInvitationsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var revoked = await _invitationRepository.RevokePendingForUserAsync(tenantId, userId, _currentUser.UserId, cancellationToken);
        if (!revoked.Success || revoked.Data is null)
        {
            return OperationResult.Fail(revoked.Message, revoked.StatusCode, revoked.ErrorCode);
        }

        foreach (var invitationId in revoked.Data)
        {
            await _accessRevoker.PublishSafelyAsync(
                new InvitationRevoked { InvitationId = invitationId, InvitedUserId = userId, TenantId = tenantId, UserId = _currentUser.UserId },
                cancellationToken);
        }

        return OperationResult.Ok();
    }

    private async Task<OperationResult> CheckCanRemoveRolesAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var assignments = await _userRoleRepository.GetAllForTenantAsync(userId, tenantId, cancellationToken);
        if (!assignments.Success || assignments.Data is null)
        {
            return OperationResult.Fail(assignments.Message, assignments.StatusCode, assignments.ErrorCode);
        }

        foreach (var roleId in assignments.Data.Select(a => a.RoleId).Distinct())
        {
            var role = await _roleRepository.FindByIdAnyTenantAsync(roleId, cancellationToken);
            if (!role.Success || role.Data is null)
            {
                continue;
            }

            var check = await _rolePolicy.CheckAsync(
                _currentUser.UserId, userId, role.Data, RoleChange.Remove, RoleAssignmentSource.Manual, cancellationToken);
            if (!check.Success)
            {
                return OperationResult.Forbidden(
                    $"You cannot remove this member because you are not allowed to remove their role '{role.Data.Name}'.");
            }
        }

        return OperationResult.Ok();
    }
}
