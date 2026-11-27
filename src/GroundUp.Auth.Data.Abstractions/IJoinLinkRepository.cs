using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Persistence for join links (GU-68). Every method names its tenant explicitly (or looks up by token
/// hash or ID alone), because joining runs before any tenant is selected. Returned links report
/// <c>Expired</c> or <c>UsedUp</c> for an Active link past its expiry or maximum. Conditional writes
/// act only on a stored-Active link.
/// </summary>
public interface IJoinLinkRepository
{
    /// <summary>Inserts a join link with the hash of its token.</summary>
    Task<OperationResult<JoinLinkDto>> AddAsync(JoinLinkDto link, string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Finds a join link of <paramref name="tenantId"/> by ID.</summary>
    Task<OperationResult<JoinLinkDto>> GetByIdAsync(Guid tenantId, Guid joinLinkId, CancellationToken cancellationToken = default);

    /// <summary>Finds a join link by ID in any tenant (the sign-in callback).</summary>
    Task<OperationResult<JoinLinkDto>> GetByIdAnyTenantAsync(Guid joinLinkId, CancellationToken cancellationToken = default);

    /// <summary>Finds a join link by the hash of its token, in any tenant.</summary>
    Task<OperationResult<JoinLinkDto>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's join links, newest first.</summary>
    Task<OperationResult<PaginatedData<JoinLinkDto>>> ListAsync(Guid tenantId, PaginationParams paging, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the token of a stored-Active link and records who regenerated it; the old link stops working.
    /// </summary>
    /// <returns>Success, NotFound, or a 409 conflict when the link is revoked.</returns>
    Task<OperationResult> ReissueAsync(
        Guid tenantId, Guid joinLinkId, string tokenHash, Guid regeneratedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a stored-Active link.</summary>
    /// <returns>Success, NotFound, or a 409 conflict when it is already revoked.</returns>
    Task<OperationResult> RevokeAsync(Guid tenantId, Guid joinLinkId, Guid revokedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts one use if the link is Active, not expired and below its maximum, in one conditional
    /// update, so concurrent uses can never exceed the maximum.
    /// </summary>
    /// <returns>True when this use was counted.</returns>
    Task<bool> TryUseAsync(Guid joinLinkId, CancellationToken cancellationToken = default);
}
