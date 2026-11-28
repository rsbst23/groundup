using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
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
    /// <returns>
    /// The removed role assignments (empty when the membership was already inactive), NotFound if
    /// no membership exists, or a 409 conflict if the save fails.
    /// </returns>
    Task<OperationResult<List<UserRoleDto>>> DeactivateMembershipAsync(
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

    /// <summary>
    /// Inserts a membership in the tenant named by <paramref name="membership"/> (not the current
    /// tenant context). System-level bypass, used when granting SuperAdmin creates the System-tenant
    /// membership.
    /// </summary>
    /// <param name="membership">The membership to insert. <c>Id</c> is ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved membership, or a 409 conflict when the user already has one in that tenant.</returns>
    Task<OperationResult<UserTenantDto>> AddForTenantAsync(
        UserTenantDto membership,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an existing membership in <paramref name="tenantId"/> to Pending (a former member invited
    /// again). Roles are not touched. System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, NotFound, or a 409 conflict if the save fails.</returns>
    Task<OperationResult> MarkPendingAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Active and Pending members of <paramref name="tenantId"/> (by email unless sorted), with
    /// their roles and each role's sources. System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="filterParams">Paging, and the fields <c>email</c>, <c>displayName</c> (sort), <c>status</c> (sort, filter); search on email and display name. Other fields are ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<PaginatedData<TenantMemberDto>>> GetMembersAsync(
        Guid tenantId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The Active and Pending members of <paramref name="tenantId"/> who hold <paramref name="roleId"/>
    /// (any source), with the same fields, order and rows as <see cref="GetMembersAsync"/> (GU-97d).
    /// System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="filterParams">Paging, sorting, filters and search.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<PaginatedData<TenantMemberDto>>> GetRoleMembersAsync(
        Guid tenantId,
        Guid roleId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default);
}
