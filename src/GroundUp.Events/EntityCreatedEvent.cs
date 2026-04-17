namespace GroundUp.Events;

/// <summary>
/// Published when an entity is created. Carries the full created entity data.
/// </summary>
/// <typeparam name="T">The type of the created entity or DTO.</typeparam>
public record EntityCreatedEvent<T> : BaseEvent
{
    /// <summary>
    /// The created entity data.
    /// </summary>
    public required T Entity { get; init; }
}
