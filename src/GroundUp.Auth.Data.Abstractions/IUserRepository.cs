using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for User entities. Provides standard CRUD operations
/// plus custom lookup methods by external user ID and email address.
/// </summary>
public interface IUserRepository : IBaseRepository<UserDto>
{
    /// <summary>
    /// Retrieves a user by their external identity provider identifier.
    /// </summary>
    /// <param name="externalUserId">The external identity provider user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching user DTO or a NotFound result.</returns>
    Task<OperationResult<UserDto>> GetByExternalUserIdAsync(
        string externalUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a user by their email address (case-insensitive).
    /// </summary>
    /// <param name="email">The email address to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching user DTO or a NotFound result.</returns>
    Task<OperationResult<UserDto>> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
}
