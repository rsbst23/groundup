using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Membership;

/// <summary>
/// Operations that end a user's access to a tenant. Every such path goes through this service so it
/// revokes the user's existing tokens for the tenant and evicts their cached permissions, whether it
/// is called from the API or directly as an SDK.
/// <para>
/// Admin removal of a member and user deactivation (GU-68) must be added here, not done through the
/// repositories directly.
/// </para>
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// The current user leaves <paramref name="tenantId"/>. The last active TenantAdmin cannot leave.
    /// When the tenant was the session's current tenant, the session becomes tenantless (the user is
    /// not moved into another tenant) and a tenantless token is issued.
    /// </summary>
    /// <param name="tenantId">The tenant to leave.</param>
    /// <param name="originalAuthTime">When the user really signed in (<c>auth_time</c>), carried into the
    /// tenantless token so the absolute session lifetime is not restarted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Ok with the session outcome; Unauthorized with no signed-in user; NotFound when the user has no
    /// active membership in the tenant; Forbidden (with the guard's error code) for the last TenantAdmin;
    /// the repository's failure (409) when the change cannot be saved.
    /// </returns>
    Task<OperationResult<LeaveTenantResult>> LeaveTenantAsync(
        Guid tenantId,
        DateTimeOffset? originalAuthTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenants the current user is an active member of. Standard tenants are switchable and
    /// flag the session's current tenant; enterprise tenants (with their own realm) are not switchable
    /// and carry the subdomain to sign in at.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The list (empty when the user has no active membership, or when memberships or tenants cannot be
    /// read); Unauthorized with no signed-in user.
    /// </returns>
    Task<OperationResult<IReadOnlyList<MyTenantDto>>> GetMyTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="userId"/> has an active membership in <paramref name="tenantId"/>.
    /// A failed lookup counts as not a member.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True for an active member.</returns>
    Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
