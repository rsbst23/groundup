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
}
