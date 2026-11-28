using GroundUp.Auth.Core.Dtos;
using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.EventHandlers;

/// <summary>
/// Handles events when role-policy assignments are created or deleted.
/// <para>
/// Logs only. Cached permissions are not evicted here: the service that changed the role's policies
/// bumps the tenant's <see cref="PermissionCacheVersions"/> version after saving (GU-96), so every user
/// of the tenant reloads on their next request on this instance, with or without an event bus. Other
/// instances refresh when their cached sets expire. The <see cref="UserRoleChangedHandler"/> evicts a
/// single user when they gain or lose a role.
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
    /// The tenant's permission cache version was bumped by the service that made the change.
    /// </summary>
    /// <param name="event">The entity created event containing the new role-policy assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityCreatedEvent<RolePolicyDto> @event, CancellationToken cancellationToken = default)
    {
        var dto = @event.Entity;
        _logger.LogInformation(
            "RolePolicy created: RoleId={RoleId}, PolicyId={PolicyId}. Cached permissions are refreshed through the tenant's permission cache version",
            dto.RoleId,
            dto.PolicyId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a role-policy deletion event. Logs the change for observability.
    /// The tenant's permission cache version was bumped by the service that made the change.
    /// </summary>
    /// <param name="event">The entity deleted event for the removed role-policy assignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(EntityDeletedEvent<RolePolicyDto> @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "RolePolicy deleted: EntityId={EntityId}. Cached permissions are refreshed through the tenant's permission cache version",
            @event.EntityId);

        return Task.CompletedTask;
    }
}
