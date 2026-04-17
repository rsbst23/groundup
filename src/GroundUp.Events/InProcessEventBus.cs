using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GroundUp.Events;

/// <summary>
/// In-process event bus that resolves handlers from the DI container
/// and invokes them sequentially. Handler exceptions are caught, logged,
/// and swallowed — they never propagate to the publisher.
/// </summary>
public sealed class InProcessEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessEventBus> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InProcessEventBus"/>.
    /// </summary>
    /// <param name="serviceProvider">The DI container for resolving handlers.</param>
    /// <param name="logger">Logger for recording handler failures.</param>
    public InProcessEventBus(IServiceProvider serviceProvider, ILogger<InProcessEventBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var handlers = _serviceProvider.GetServices<IEventHandler<T>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(@event, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Event handler {HandlerType} failed for event {EventType} (EventId: {EventId})",
                    handler.GetType().Name,
                    typeof(T).Name,
                    @event.EventId);
            }
        }
    }
}
