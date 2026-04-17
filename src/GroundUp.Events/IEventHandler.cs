namespace GroundUp.Events;

/// <summary>
/// Defines a handler for a specific event type. Implement this interface
/// to subscribe to events published via <see cref="IEventBus"/>.
/// </summary>
/// <typeparam name="T">The event type to handle.</typeparam>
public interface IEventHandler<in T> where T : IEvent
{
    /// <summary>
    /// Handles the specified event.
    /// </summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(T @event, CancellationToken cancellationToken = default);
}
