using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// What an <see cref="IRoleAssignmentRule"/> is asked to judge.
/// </summary>
/// <param name="CallerUserId">The user making the change (for invitations and join links: their creator).</param>
/// <param name="TargetUserId">
/// The user receiving or losing the role. <see cref="Guid.Empty"/> when the change is checked in
/// advance without a target (<see cref="IUserRoleService.CanGrantRoleAsync"/>, creating an invitation
/// or join link before the invitee is known).
/// </param>
/// <param name="Role">The role being granted or removed.</param>
/// <param name="TenantId">The tenant the role belongs to.</param>
/// <param name="Change">Assign or remove.</param>
/// <param name="Source">How the role is being granted (Manual for an admin change).</param>
public sealed record RoleAssignmentContext(
    Guid CallerUserId,
    Guid TargetUserId,
    RoleDto Role,
    Guid TenantId,
    RoleChange Change,
    RoleAssignmentSource Source);
