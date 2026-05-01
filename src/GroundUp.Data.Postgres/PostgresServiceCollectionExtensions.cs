using GroundUp.Data.Postgres.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Extension methods for registering GroundUp Postgres infrastructure
/// with the dependency injection container.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GroundUp Postgres infrastructure: DbContext with Npgsql,
    /// audit and soft delete interceptors, and the data seeder runner.
    /// </summary>
    /// <typeparam name="TContext">
    /// The consuming application's DbContext type. Must inherit from <see cref="GroundUpDbContext"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddGroundUpPostgres<TContext>(
        this IServiceCollection services,
        string connectionString)
        where TContext : GroundUpDbContext
    {
        // Register interceptors as singletons
        services.AddSingleton<AuditableInterceptor>();
        services.AddSingleton<SoftDeleteInterceptor>();

        // Register DbContext with Npgsql and interceptors
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<AuditableInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>());
        });

        // Register GroundUpDbContext as a forwarding service so framework services
        // (e.g., SettingsService, DefaultScopeChainProvider) can resolve the base type.
        services.AddScoped<GroundUpDbContext>(sp => sp.GetRequiredService<TContext>());

        // Register DataSeederRunner as hosted service
        services.AddHostedService<DataSeederRunner>();

        return services;
    }
}
