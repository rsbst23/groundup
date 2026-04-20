using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Services;

/// <summary>
/// Extension methods for registering GroundUp service layer dependencies
/// in the Microsoft DI container.
/// </summary>
public static class ServicesServiceCollectionExtensions
{
    /// <summary>
    /// Scans the specified assembly for FluentValidation <see cref="IValidator{T}"/>
    /// implementations and registers them as scoped services in the DI container.
    /// </summary>
    /// <param name="services">The service collection to add validators to.</param>
    /// <param name="assembly">The assembly to scan for validator implementations.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddGroundUpServices(
        this IServiceCollection services,
        Assembly assembly)
    {
        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);
        return services;
    }
}
