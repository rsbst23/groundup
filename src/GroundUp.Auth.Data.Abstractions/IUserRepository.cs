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

    /// <summary>
    /// Returns every user with this email (case-insensitive) that was not merged into another user,
    /// linked or pending, oldest first.
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<UserDto>>> GetAllByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the pending users with this email (case-insensitive): no identity-provider id yet and
    /// not merged into another user.
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<UserDto>>> GetUnlinkedByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a pending user to an identity-provider identity: sets <c>ExternalUserId</c> on the user
    /// and on its memberships that have none, and replaces the placeholder display name (the email)
    /// with <paramref name="displayName"/>. Only succeeds while the user is still unlinked, so two
    /// concurrent claims cannot both win.
    /// </summary>
    /// <param name="userId">The pending user.</param>
    /// <param name="externalUserId">The identity-provider subject.</param>
    /// <param name="displayName">The display name from the identity provider, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The linked user, or a 409 conflict when it was already linked (or the identity belongs to another user).</returns>
    Task<OperationResult<UserDto>> LinkIdentityAsync(
        Guid userId,
        string externalUserId,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges pending user <paramref name="fromUserId"/> into <paramref name="toUserId"/> in one save.
    /// For each of the pending user's memberships: when the target has none in that tenant, the
    /// membership moves to the target; when the target is an Active member, the pending membership
    /// and its roles are dropped (the target's roles do not change); otherwise the target's
    /// membership takes the pending status and the pending roles move to the target. The pending user
    /// is kept, marked as merged and inactive.
    /// </summary>
    /// <param name="fromUserId">The pending user.</param>
    /// <param name="toUserId">The existing user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenants whose memberships or roles changed for the target.</returns>
    Task<OperationResult<List<Guid>>> MergePendingUserAsync(
        Guid fromUserId,
        Guid toUserId,
        CancellationToken cancellationToken = default);
}
