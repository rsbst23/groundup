using GroundUp.Events;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Events;

/// <summary>
/// Publishes an event after a saved change, when an event bus is registered. A handler failure is
/// logged and does not change the outcome: the change is already saved (as in <c>BaseService</c>).
/// </summary>
internal static class SafeEventPublisher
{
    /// <summary>
    /// Publishes <paramref name="event"/> through <paramref name="eventBus"/>, or does nothing when it is null.
    /// </summary>
    public static async Task PublishAsync<TEvent>(
        IEventBus? eventBus,
        ILogger logger,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        if (eventBus is null)
        {
            return;
        }

        try
        {
            await eventBus.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Publishing {EventType} failed; the change is saved", typeof(TEvent).Name);
        }
    }
}
