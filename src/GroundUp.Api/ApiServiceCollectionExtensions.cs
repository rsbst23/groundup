using GroundUp.Core;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Api;

/// <summary>
/// Extension methods for registering GroundUp API layer services
/// with the dependency injection container.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers GroundUp API layer services including tenant context,
    /// HTTP context accessor, and other infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddGroundUpApi(this IServiceCollection services)
    {
        // Dual registration: middleware resolves concrete TenantContext to set TenantId,
        // repositories resolve ITenantContext to read it — both get the same scoped instance.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddHttpContextAccessor();

        return services;
    }
}
