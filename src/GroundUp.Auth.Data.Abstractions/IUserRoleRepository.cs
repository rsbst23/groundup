using GroundUp.Auth.Core.Dtos;
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
}
