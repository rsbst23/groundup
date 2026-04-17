namespace GroundUp.Events;

/// <summary>
/// Published when an entity is deleted. Carries the deleted entity's identifier.
/// </summary>
/// <typeparam name="T">The type parameter for consistency with other lifecycle events.</typeparam>
public record EntityDeletedEvent<T> : BaseEvent
{
    /// <summary>
    /// The unique identifier of the deleted entity.
    /// </summary>
    public required Guid EntityId { get; init; }
}
