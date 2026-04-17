namespace GroundUp.Events;

/// <summary>
/// Published when an entity is updated. Carries the updated entity data.
/// </summary>
/// <typeparam name="T">The type of the updated entity or DTO.</typeparam>
public record EntityUpdatedEvent<T> : BaseEvent
{
    /// <summary>
    /// The updated entity data.
    /// </summary>
    public required T Entity { get; init; }
}
