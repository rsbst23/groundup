using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Membership;

/// <summary>
/// Ends a user's current access to a tenant after a change that takes rights away (leaving, being
/// removed, losing a role): publishes a role-removed event per removed assignment, records a token
/// revocation cutoff (GU-71) and evicts cached permissions. Run only after the change is saved.
/// </summary>
public sealed class TenantAccessRevoker
{
    private readonly ITokenRevocationStore _revocationStore;
    private readonly IMemoryCache _cache;
    private readonly IEventBus? _eventBus;
    private readonly ILogger<TenantAccessRevoker> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TenantAccessRevoker"/>.
    /// </summary>
    /// <param name="revocationStore">Records token revocation cutoffs.</param>
    /// <param name="cache">The cache holding resolved permissions.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="eventBus">
    /// The event bus, when the application registered one (<c>AddGroundUpEvents</c>). Without it no events
    /// are published; cached permissions are still evicted directly.
    /// </param>
    public TenantAccessRevoker(
        ITokenRevocationStore revocationStore,
        IMemoryCache cache,
        ILogger<TenantAccessRevoker> logger,
        IEventBus? eventBus = null)
    {
        _revocationStore = revocationStore;
        _cache = cache;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Publishes <see cref="EntityDeletedEvent{T}"/> for each removed assignment, records the
    /// revocation cutoff and evicts cached permissions. Failures of the store or of event handlers are
    /// logged and do not undo the change: it is already saved, and the token stops working within its
    /// lifetime.
    /// </summary>
    /// <param name="userId">The user whose access ends.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="removedRoles">The assignments that were removed, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EndAccessAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<UserRoleDto>? removedRoles = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var row in removedRoles ?? [])
        {
            await PublishSafelyAsync(new EntityDeletedEvent<UserRoleDto>
            {
                EntityId = row.Id,
                // The affected user and tenant, which the permission-cache handler reads
                TenantId = tenantId,
                UserId = userId
            }, cancellationToken);
        }

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

        EvictPermissions(userId, tenantId);
    }

    /// <summary>
    /// Evicts the user's cached permissions for the tenant, so the next check reads the database.
    /// </summary>
    public void EvictPermissions(Guid userId, Guid tenantId)
        => _cache.Remove(PermissionCacheKeys.For(userId, tenantId));

    /// <summary>
    /// Publishes an event after a saved change; a handler failure is logged and does not change the
    /// outcome (as in <c>BaseService</c>).
    /// </summary>
    internal async Task PublishSafelyAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        if (_eventBus is null)
        {
            return;
        }

        try
        {
            await _eventBus.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Publishing {EventType} failed; the change is saved", typeof(TEvent).Name);
        }
    }
}
