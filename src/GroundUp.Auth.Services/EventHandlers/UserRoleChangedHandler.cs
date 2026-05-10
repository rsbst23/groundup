using GroundUp.Auth.Core.Dtos;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.EventHandlers;

/// <summary>
/// Handles cache invalidation when user-role assignments are created or deleted.
/// Evicts the permission cache entry for the affected user and tenant so that
/// subsequent permission checks reflect the updated role assignments.
/// </summary>
public sealed class UserRoleChangedHandler :
    IEventHandler<EntityCreatedEvent<UserRoleDto>>,
    IEventHandler<EntityDeletedEvent<UserRoleDto>>
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserRoleChangedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRoleChangedHandler"/> class.
    /// </summary>
    /// <param name="cache">The in-memory cache storing permission sets.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public UserRoleChangedHandler(IMemoryCache cache, ILogger<UserRoleChangedHandler> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Handles a user-role creation event by evicting the affected user's permission cache entry.
    /// </summary>
    /// <param name="event">The entity created event containing the new user-role assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityCreatedEvent<UserRoleDto> @event, CancellationToken cancellationToken = default)
    {
        var dto = @event.Entity;
        var cacheKey = $"permissions:{dto.UserId}:{dto.TenantId}";

        _cache.Remove(cacheKey);
        _logger.LogDebug("Evicted permission cache for key {CacheKey} due to UserRole creation", cacheKey);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a user-role deletion event by evicting the affected user's permission cache entry.
    /// Uses the UserId and TenantId from the base event metadata populated by the publishing service.
    /// </summary>
    /// <param name="event">The entity deleted event for the removed user-role assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityDeletedEvent<UserRoleDto> @event, CancellationToken cancellationToken = default)
    {
        if (@event.UserId is null || @event.TenantId is null)
        {
            _logger.LogWarning(
                "Cannot evict permission cache for deleted UserRole {EntityId}: UserId or TenantId not available in event metadata",
                @event.EntityId);
            return Task.CompletedTask;
        }

        var cacheKey = $"permissions:{@event.UserId}:{@event.TenantId}";

        _cache.Remove(cacheKey);
        _logger.LogDebug("Evicted permission cache for key {CacheKey} due to UserRole deletion", cacheKey);

        return Task.CompletedTask;
    }
}
