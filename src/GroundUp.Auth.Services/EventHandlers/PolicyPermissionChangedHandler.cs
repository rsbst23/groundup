using GroundUp.Auth.Core.Dtos;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.EventHandlers;

/// <summary>
/// Handles events when policy-permission assignments are created or deleted.
/// <para>
/// Cache invalidation trade-off: Since <c>IMemoryCache</c> does not support pattern-based eviction
/// and determining all affected users requires cascading through roles and user-role assignments,
/// this handler relies on TTL-based cache expiry for permission updates. Policy-permission changes
/// are admin operations that happen rarely, and the default 15-minute TTL ensures eventual consistency.
/// The <see cref="UserRoleChangedHandler"/> handles the most common case (user gains/loses a role)
/// with immediate cache eviction since the userId is directly available.
/// </para>
/// </summary>
public sealed class PolicyPermissionChangedHandler :
    IEventHandler<EntityCreatedEvent<PolicyPermissionDto>>,
    IEventHandler<EntityDeletedEvent<PolicyPermissionDto>>
{
    private readonly ILogger<PolicyPermissionChangedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyPermissionChangedHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public PolicyPermissionChangedHandler(ILogger<PolicyPermissionChangedHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles a policy-permission creation event. Logs the change for observability.
    /// Cache invalidation is handled by TTL expiry since determining all affected users
    /// requires cascading through roles and user-role assignments.
    /// </summary>
    /// <param name="event">The entity created event containing the new policy-permission assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityCreatedEvent<PolicyPermissionDto> @event, CancellationToken cancellationToken = default)
    {
        var dto = @event.Entity;
        _logger.LogInformation(
            "PolicyPermission created: PolicyId={PolicyId}, PermissionId={PermissionId}. Permission cache will refresh at TTL expiry",
            dto.PolicyId,
            dto.PermissionId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a policy-permission deletion event. Logs the change for observability.
    /// Cache invalidation is handled by TTL expiry since determining all affected users
    /// requires cascading through roles and user-role assignments.
    /// </summary>
    /// <param name="event">The entity deleted event for the removed policy-permission assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityDeletedEvent<PolicyPermissionDto> @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "PolicyPermission deleted: EntityId={EntityId}. Permission cache will refresh at TTL expiry",
            @event.EntityId);

        return Task.CompletedTask;
    }
}
