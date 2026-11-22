using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Membership;

/// <summary>
/// Default <see cref="IMembershipService"/>. After a membership change is saved it records a token
/// revocation cutoff for the user and tenant (<see cref="ITokenRevocationStore"/>) and evicts the user's
/// cached permissions for that tenant.
/// </summary>
public sealed class MembershipService : IMembershipService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly LastAdminGuard _lastAdminGuard;
    private readonly ITokenRevocationStore _revocationStore;
    private readonly IMemoryCache _cache;
    private readonly IAuthSessionService _authSessionService;
    private readonly ILogger<MembershipService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MembershipService"/>.
    /// </summary>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The session's current tenant.</param>
    /// <param name="userTenantRepository">Membership persistence.</param>
    /// <param name="lastAdminGuard">Keeps at least one active TenantAdmin per tenant.</param>
    /// <param name="revocationStore">Records token revocation cutoffs.</param>
    /// <param name="cache">The cache holding resolved permissions.</param>
    /// <param name="authSessionService">Issues the tenantless session token.</param>
    /// <param name="logger">Logger.</param>
    public MembershipService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IUserTenantRepository userTenantRepository,
        LastAdminGuard lastAdminGuard,
        ITokenRevocationStore revocationStore,
        IMemoryCache cache,
        IAuthSessionService authSessionService,
        ILogger<MembershipService> logger)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _userTenantRepository = userTenantRepository;
        _lastAdminGuard = lastAdminGuard;
        _revocationStore = revocationStore;
        _cache = cache;
        _authSessionService = authSessionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<LeaveTenantResult>> LeaveTenantAsync(
        Guid tenantId,
        DateTimeOffset? originalAuthTime,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return OperationResult<LeaveTenantResult>.Unauthorized();
        }

        // 1. The user must have an active membership in the tenant
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, cancellationToken);
        var isMember = membershipsResult.Success
            && membershipsResult.Data is not null
            && membershipsResult.Data.Any(m => m.TenantId == tenantId && m.IsActive);
        if (!isMember)
        {
            return OperationResult<LeaveTenantResult>.NotFound("User is not a member of this tenant.");
        }

        // 2. Only the last active TenantAdmin is blocked from leaving
        var guardResult = await _lastAdminGuard.CanLeaveTenantAsync(userId, tenantId, cancellationToken);
        if (!guardResult.Success)
        {
            return OperationResult<LeaveTenantResult>.Fail(guardResult.Message, 403, guardResult.ErrorCode);
        }

        // 3. Deactivate. Targets the tenant explicitly: the session may be scoped to a different tenant.
        var deactivateResult = await _userTenantRepository.DeactivateMembershipAsync(userId, tenantId, cancellationToken);
        if (!deactivateResult.Success)
        {
            return OperationResult<LeaveTenantResult>.Fail(
                deactivateResult.Message, deactivateResult.StatusCode, deactivateResult.ErrorCode);
        }

        // 4. Access has ended: revoke tokens issued so far and drop cached permissions
        await EndAccessAsync(userId, tenantId, cancellationToken);

        // 5. Session outcome
        Guid? currentTenantId = _tenantContext.TenantId == Guid.Empty ? null : _tenantContext.TenantId;
        if (currentTenantId != tenantId)
        {
            // Left a different tenant (or the session was already tenantless): the session is unchanged
            return OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(currentTenantId, SessionEnded: false, SessionToken: null));
        }

        // The user left the tenant they were working in. They are not moved into another tenant: the
        // session becomes tenantless and the client asks them to choose (or create) one.
        var tenantlessResult = await _authSessionService.IssueTenantlessSessionAsync(userId, originalAuthTime);
        var sessionToken = tenantlessResult.Success && !string.IsNullOrEmpty(tenantlessResult.Data)
            ? tenantlessResult.Data
            : null;

        return OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(null, SessionEnded: true, sessionToken));
    }

    /// <summary>
    /// Records the revocation cutoff and evicts cached permissions. Runs only after the change is saved.
    /// A store failure is logged and does not undo the leave: the membership is already gone, and the
    /// token stops working within the token lifetime.
    /// </summary>
    private async Task EndAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _revocationStore.RevokeTenantAccessAsync(userId, tenantId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Could not record token revocation for user {UserId} in tenant {TenantId}; existing tokens stay valid until they expire",
                userId, tenantId);
        }

        _cache.Remove(PermissionCacheKeys.For(userId, tenantId));
    }
}
