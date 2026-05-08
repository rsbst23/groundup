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
}
