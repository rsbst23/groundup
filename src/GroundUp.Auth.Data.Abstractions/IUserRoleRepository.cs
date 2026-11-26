using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for UserRole junction entities. Provides standard CRUD operations
/// plus a method for querying all role assignments for a user within the current tenant.
/// </summary>
public interface IUserRoleRepository : IBaseRepository<UserRoleDto>
{
    /// <summary>
    /// Retrieves all role assignments for a given user within the current tenant.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of user-role DTOs for the specified user in the current tenant.</returns>
    Task<OperationResult<List<UserRoleDto>>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all role assignments for a given user within an explicit tenant,
    /// bypassing the ambient <see cref="GroundUp.Core.Abstractions.ITenantContext"/> filter.
    /// Used by token generation, where the target tenant may differ from the current
    /// request's tenant context (e.g., during sign-in when no tenant is selected yet,
    /// or when refreshing a token for a different tenant).
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The target tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of user-role DTOs for the specified user in the specified tenant, including role names.</returns>
    Task<OperationResult<List<UserRoleDto>>> GetByUserIdForTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all system-level role assignments for a user, bypassing tenant filtering.
    /// Returns UserRole records where the associated Role has RoleType == System.
    /// Includes the Role name for direct comparison without additional lookups.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of user-role DTOs for system roles, regardless of tenant context.</returns>
    Task<OperationResult<List<UserRoleDto>>> GetSystemRolesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all users who hold the TenantAdmin role in the specified tenant and still have
    /// an active membership in it (an admin who left the tenant is not counted).
    /// Used by the last-admin guard to determine whether a TenantAdmin removal is safe.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to check for TenantAdmin holders.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of user-role DTOs for users holding TenantAdmin in the specified tenant.</returns>
    Task<OperationResult<List<UserRoleDto>>> GetTenantAdminHoldersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the IDs of the users who hold <paramref name="roleId"/> through an Active membership
    /// in the role's tenant, each once. Used by the last-SuperAdmin guard.
    /// System-level bypass: ignores the current tenant context.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<Guid>>> GetActiveHolderIdsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every assignment row of <paramref name="roleId"/> to <paramref name="userId"/> in
    /// <paramref name="tenantId"/>, one per source, whatever the membership status.
    /// System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<UserRoleDto>>> GetAssignmentsAsync(
        Guid userId,
        Guid roleId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every assignment row of <paramref name="userId"/> in <paramref name="tenantId"/>,
    /// every role and source, with role names, whatever the membership status (a pending member's
    /// roles included). System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<UserRoleDto>>> GetAllForTenantAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// Inserts an assignment in the tenant named by <paramref name="assignment"/> (not the current
    /// tenant context), keeping its source. System-level bypass, used by the role service, which
    /// checks who may grant the role first.
    /// </summary>
    /// <param name="assignment">The assignment to insert. <c>Id</c> is ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved assignment, or a 409 conflict when the same role and source already exist.</returns>
    Task<OperationResult<UserRoleDto>> AddForTenantAsync(
        UserRoleDto assignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the user's assignments of <paramref name="roleId"/> in <paramref name="tenantId"/>,
    /// either from every source or from <paramref name="source"/> only.
    /// System-level bypass: targets the tenant explicitly.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleId">The role identifier, or null for every role.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="source">Only this source, or null for every source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deleted assignments (empty when there were none).</returns>
    Task<OperationResult<List<UserRoleDto>>> RemoveAssignmentsAsync(
        Guid userId,
        Guid? roleId,
        Guid tenantId,
        RoleAssignmentSource? source,
        CancellationToken cancellationToken = default);
}
