namespace GroundUp.Api;

using GroundUp.Api.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers GroundUp infrastructure health checks.
/// </summary>
public static class HealthCheckServiceCollectionExtensions
{
    public static IServiceCollection AddGroundUpHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<MasterKeyHealthCheck>("master-key", tags: new[] { "infrastructure" });

        return services;
    }
}
