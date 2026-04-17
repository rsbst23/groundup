namespace GroundUp.Events;

/// <summary>
/// Base interface for all domain events in the GroundUp framework.
/// Carries event identity and multi-tenant context metadata.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    DateTime OccurredAt { get; }

    /// <summary>
    /// The tenant context in which the event occurred. Null if not in a tenant context.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// The user who triggered the event. Null if triggered by a system process.
    /// </summary>
    Guid? UserId { get; }
}
