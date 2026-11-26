using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Persistence for invitations (GU-68). Every method names its tenant explicitly (or looks up by
/// token hash), because acceptance runs before any tenant is selected. Returned invitations report
/// <c>Expired</c> for a Pending invitation past its expiry. Conditional writes act only on a stored
/// Pending invitation (expired ones included), so concurrent changes cannot both win.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>Inserts an invitation with the hash of its token.</summary>
    /// <returns>The invitation, or a 409 conflict when the email already has a pending invitation to the tenant.</returns>
    Task<OperationResult<InvitationDto>> AddAsync(
        InvitationDto invitation, string normalizedEmail, string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Finds an invitation of <paramref name="tenantId"/> by ID.</summary>
    Task<OperationResult<InvitationDto>> GetByIdAsync(Guid tenantId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>Finds an invitation by the hash of its token, in any tenant.</summary>
    Task<OperationResult<InvitationDto>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's invitations, newest first.</summary>
    Task<OperationResult<PaginatedData<InvitationDto>>> ListAsync(
        Guid tenantId, PaginationParams paging, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the token and expiry of a stored-Pending invitation, and records who resent it.
    /// The old token stops working.
    /// </summary>
    /// <returns>Success, NotFound, or a 409 conflict when the invitation is no longer pending.</returns>
    Task<OperationResult> ReissueAsync(
        Guid tenantId, Guid invitationId, string tokenHash, DateTime expiresAt, Guid resentByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a stored-Pending invitation.</summary>
    /// <returns>Success, NotFound, or a 409 conflict when the invitation is no longer pending.</returns>
    Task<OperationResult> RevokeAsync(
        Guid tenantId, Guid invitationId, Guid revokedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every stored-Pending invitation of <paramref name="userId"/> to <paramref name="tenantId"/>
    /// (used when a pending member is removed).
    /// </summary>
    /// <returns>The revoked invitations' IDs.</returns>
    Task<OperationResult<List<Guid>>> RevokePendingForUserAsync(
        Guid tenantId, Guid userId, Guid revokedByUserId, CancellationToken cancellationToken = default);
}
