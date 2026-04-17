namespace GroundUp.Events;

/// <summary>
/// Abstract base record for all domain events. Provides sensible defaults
/// for <see cref="EventId"/> and <see cref="OccurredAt"/>.
/// Concrete events inherit from this record and add domain-specific properties.
/// </summary>
public abstract record BaseEvent : IEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <inheritdoc />
    public Guid? TenantId { get; init; }

    /// <inheritdoc />
    public Guid? UserId { get; init; }
}
