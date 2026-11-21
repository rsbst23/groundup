using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Events;

/// <summary>
/// Extension methods for registering GroundUp event bus services
/// with the dependency injection container.
/// </summary>
public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-process event bus as a scoped implementation of <see cref="IEventBus"/>.
    /// <para>
    /// Scoped, not singleton: the bus resolves handlers from the provider it was created with,
    /// and handlers are usually scoped (they use a DbContext, caches, the current user). A
    /// singleton bus would resolve them from the root provider, which throws under scope
    /// validation (Development) and otherwise keeps scoped services alive for the app's lifetime.
    /// A scoped bus runs handlers in the publisher's scope. Singleton handlers still resolve.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGroundUpEvents(this IServiceCollection services)
    {
        services.AddScoped<IEventBus, InProcessEventBus>();
        return services;
    }
}
