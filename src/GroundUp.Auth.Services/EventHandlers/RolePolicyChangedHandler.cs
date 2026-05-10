using GroundUp.Auth.Core.Dtos;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.EventHandlers;

/// <summary>
/// Handles events when role-policy assignments are created or deleted.
/// <para>
/// Cache invalidation trade-off: Since <c>IMemoryCache</c> does not support pattern-based eviction
/// and <c>IUserRoleRepository</c> does not expose a "get users by role" method, this handler
/// relies on TTL-based cache expiry for permission updates. Role-policy changes are admin operations
/// that happen rarely, and the default 15-minute TTL ensures eventual consistency.
/// The <see cref="UserRoleChangedHandler"/> handles the most common case (user gains/loses a role)
/// with immediate cache eviction since the userId is directly available.
/// </para>
/// </summary>
public sealed class RolePolicyChangedHandler :
    IEventHandler<EntityCreatedEvent<RolePolicyDto>>,
    IEventHandler<EntityDeletedEvent<RolePolicyDto>>
{
    private readonly ILogger<RolePolicyChangedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RolePolicyChangedHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RolePolicyChangedHandler(ILogger<RolePolicyChangedHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles a role-policy creation event. Logs the change for observability.
    /// Cache invalidation is handled by TTL expiry since we cannot efficiently
    /// determine all affected users without a "get users by role" query.
    /// </summary>
    /// <param name="event">The entity created event containing the new role-policy assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityCreatedEvent<RolePolicyDto> @event, CancellationToken cancellationToken = default)
    {
        var dto = @event.Entity;
        _logger.LogInformation(
            "RolePolicy created: RoleId={RoleId}, PolicyId={PolicyId}. Permission cache will refresh at TTL expiry",
            dto.RoleId,
            dto.PolicyId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a role-policy deletion event. Logs the change for observability.
    /// Cache invalidation is handled by TTL expiry since we cannot efficiently
    /// determine all affected users without a "get users by role" query.
    /// </summary>
    /// <param name="event">The entity deleted event for the removed role-policy assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityDeletedEvent<RolePolicyDto> @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "RolePolicy deleted: EntityId={EntityId}. Permission cache will refresh at TTL expiry",
            @event.EntityId);

        return Task.CompletedTask;
    }
}
