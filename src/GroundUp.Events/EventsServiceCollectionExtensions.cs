using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Events;

/// <summary>
/// Extension methods for registering GroundUp event bus services
/// with the dependency injection container.
/// </summary>
public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-process event bus as a singleton implementation of <see cref="IEventBus"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGroundUpEvents(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, InProcessEventBus>();
        return services;
    }
}
