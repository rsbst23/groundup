namespace GroundUp.Events;

/// <summary>
/// Defines the contract for publishing domain events to registered handlers.
/// Implementations may dispatch events in-process, via message broker, or both.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered handlers for the event type.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="event">The event instance to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when all handlers have been invoked.</returns>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent;
}
