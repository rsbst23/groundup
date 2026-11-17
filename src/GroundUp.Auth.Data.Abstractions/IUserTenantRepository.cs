using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for UserTenant junction entities. Provides standard CRUD operations
/// plus methods for querying user-tenant memberships within and across tenants.
/// </summary>
public interface IUserTenantRepository : IBaseRepository<UserTenantDto>
{
    /// <summary>
    /// Retrieves the tenant membership record for a given user within the current tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching user-tenant DTO or a NotFound result.</returns>
    Task<OperationResult<UserTenantDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all tenant memberships for a user across all tenants.
    /// This is a system-level bypass that ignores tenant filtering, required for
    /// the multi-tenant selection auth flow where a user needs to see all their memberships.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all user-tenant DTOs for the specified user across all tenants.</returns>
    Task<OperationResult<List<UserTenantDto>>> GetAllMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a user's membership in the specified tenant (the user leaves the tenant) and,
    /// in the same save, removes all of the user's role assignments in that tenant, so rejoining
    /// later starts without the old roles.
    /// This is a system-level bypass that targets <paramref name="tenantId"/> explicitly instead
    /// of the current tenant context, because a user may leave a tenant other than the one their
    /// session is scoped to. Callers must verify the user is allowed to leave first.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant whose membership is deactivated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, NotFound if no membership exists, or a 409 conflict if the save fails.</returns>
    Task<OperationResult> DeactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a user's previously deactivated membership in the specified tenant (the user
    /// rejoins a tenant they had left). A user has at most one membership row per tenant, so
    /// rejoining must reuse that row instead of inserting a new one. This is a system-level
    /// bypass that targets <paramref name="tenantId"/> explicitly instead of the current tenant
    /// context. Roles are not restored: leaving removed the user's role assignments in that
    /// tenant, so the rejoined member has none until they are granted again.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant whose membership is reactivated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success (also when the membership is already active), NotFound if no membership row exists,
    /// or a 409 conflict if the save fails.
    /// </returns>
    Task<OperationResult> ReactivateMembershipAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
