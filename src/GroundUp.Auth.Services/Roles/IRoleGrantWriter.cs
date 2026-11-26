using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// The system path for role assignments made by the module itself: organization provisioning, the
/// default role, invitations and join links. It does not check who the caller is; each caller checks
/// what it needs first (for example, an invitation checks that its creator may grant the role).
/// Internal so that code outside the module goes through <see cref="IUserRoleService"/>.
/// Implemented by the same class as <see cref="IUserRoleService"/>, so events and cache eviction
/// happen in one place.
/// </summary>
internal interface IRoleGrantWriter
{
    /// <summary>
    /// Saves an assignment of <paramref name="roleId"/> to <paramref name="userId"/> in
    /// <paramref name="tenantId"/> from <paramref name="source"/>. An existing assignment from the same
    /// source is returned unchanged.
    /// </summary>
    /// <param name="userId">The user receiving the role. Must be an Active or Pending member of the tenant.</param>
    /// <param name="roleId">The role. Must belong to <paramref name="tenantId"/>.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="source">Why the role is granted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="deferEvents">
    /// True inside a caller's transaction: nothing is published, and the caller calls
    /// <see cref="PublishGrantedAsync"/> after committing. Otherwise the event is published and the
    /// cache evicted right away.
    /// </param>
    Task<OperationResult<RoleGrantResult>> GrantAsync(
        Guid userId,
        Guid roleId,
        Guid tenantId,
        RoleAssignmentSource source,
        bool deferEvents = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the role-granted event and evicts cached permissions for a grant saved with
    /// <c>deferEvents</c>. Does nothing when the grant created nothing new.
    /// </summary>
    Task PublishGrantedAsync(RoleGrantResult grant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the user's assignments from <paramref name="source"/> only, in every role, and
    /// publishes the role-removed events. Used when an invitation is revoked. No caller rules or
    /// last-admin guard: automatic processes only remove what they added.
    /// </summary>
    Task<OperationResult> RemoveBySourceAsync(
        Guid userId,
        Guid tenantId,
        RoleAssignmentSource source,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of <see cref="IRoleGrantWriter.GrantAsync"/>.
/// </summary>
/// <param name="Assignment">The saved (or already existing) assignment.</param>
/// <param name="Created">False when the assignment already existed.</param>
internal sealed record RoleGrantResult(UserRoleDto Assignment, bool Created);
