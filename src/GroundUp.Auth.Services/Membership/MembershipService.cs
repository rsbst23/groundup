using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
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
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly IAuthSessionService _authSessionService;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<MembershipService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MembershipService"/>.
    /// </summary>
    /// <param name="currentUser">The signed-in user.</param>
    /// <param name="tenantContext">The session's current tenant.</param>
    /// <param name="userTenantRepository">Membership persistence.</param>
    /// <param name="lastAdminGuard">Keeps at least one active TenantAdmin per tenant.</param>
    /// <param name="accessRevoker">Publishes role-removed events, records the revocation cutoff and evicts cached permissions.</param>
    /// <param name="authSessionService">Issues the tenantless session token.</param>
    /// <param name="tenantRepository">Loads tenant details for the tenant list.</param>
    /// <param name="logger">Logger.</param>
    public MembershipService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IUserTenantRepository userTenantRepository,
        LastAdminGuard lastAdminGuard,
        TenantAccessRevoker accessRevoker,
        IAuthSessionService authSessionService,
        ITenantRepository tenantRepository,
        ILogger<MembershipService> logger)
    {
        _tenantRepository = tenantRepository;
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _userTenantRepository = userTenantRepository;
        _lastAdminGuard = lastAdminGuard;
        _accessRevoker = accessRevoker;
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
        if (!await IsActiveMemberAsync(userId, tenantId, cancellationToken))
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

        // 4. Access has ended: role-removed events, revoke tokens issued so far, drop cached permissions
        await _accessRevoker.EndAccessAsync(userId, tenantId, deactivateResult.Data, cancellationToken);

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

    /// <inheritdoc />
    public async Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, cancellationToken);
        return membershipsResult.Success
            && membershipsResult.Data is not null
            && membershipsResult.Data.Any(m => m.TenantId == tenantId && m.IsActive);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<MyTenantDto>>> GetMyTenantsAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return OperationResult<IReadOnlyList<MyTenantDto>>.Unauthorized();
        }

        IReadOnlyList<MyTenantDto> none = [];

        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, cancellationToken);
        if (!membershipsResult.Success || membershipsResult.Data is null)
        {
            return OperationResult<IReadOnlyList<MyTenantDto>>.Ok(none);
        }

        var tenantIds = membershipsResult.Data.Where(m => m.IsActive).Select(m => m.TenantId).ToList();
        if (tenantIds.Count == 0)
        {
            return OperationResult<IReadOnlyList<MyTenantDto>>.Ok(none);
        }

        // System-level read: the user's tenants span tenant boundaries
        var tenantsResult = await _tenantRepository.GetByIdsBypassFilterAsync(tenantIds, cancellationToken);
        if (!tenantsResult.Success || tenantsResult.Data is null)
        {
            return OperationResult<IReadOnlyList<MyTenantDto>>.Ok(none);
        }

        var currentTenantId = _tenantContext.TenantId;
        IReadOnlyList<MyTenantDto> tenants = tenantsResult.Data
            .Select(t => string.IsNullOrEmpty(t.RealmName)
                ? new MyTenantDto(t.Id, t.Name, t.Slug, "standard", Switchable: true, Current: t.Id == currentTenantId, Subdomain: null)
                : new MyTenantDto(t.Id, t.Name, t.Slug, "enterprise", Switchable: false, Current: null, Subdomain: t.Slug))
            .ToList();

        return OperationResult<IReadOnlyList<MyTenantDto>>.Ok(tenants);
    }
}
