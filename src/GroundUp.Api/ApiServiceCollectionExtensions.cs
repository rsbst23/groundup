using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Api;

/// <summary>
/// Extension methods for registering GroundUp API layer services
/// with the dependency injection container.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers GroundUp API layer services. Currently a placeholder
    /// that returns the service collection for method chaining.
    /// As the API layer grows, registrations will be added here.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddGroundUpApi(this IServiceCollection services)
    {
        return services;
    }
}
