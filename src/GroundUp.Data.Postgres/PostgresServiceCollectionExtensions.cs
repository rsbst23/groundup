using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Repositories.Bootstrap;
using GroundUp.Repositories.Setup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Extension methods for registering GroundUp Postgres infrastructure
/// with the dependency injection container.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GroundUp Postgres infrastructure: DbContext with Npgsql,
    /// audit and soft delete interceptors, the data seeder runner, and the provider services
    /// (<see cref="IUniqueConstraintDetector"/>, <see cref="IDatabaseMigrator"/>).
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

        // Register GroundUpDbContext as a forwarding service so the framework's repositories
        // can resolve the base type.
        services.AddScoped<GroundUpDbContext>(sp => sp.GetRequiredService<TContext>());

        // Provider services used by repositories and services through Data.Abstractions.
        // TryAdd: the auth module registers the same detector, and either may be added first.
        services.TryAddSingleton<IUniqueConstraintDetector, PostgresUniqueConstraintDetector>();
        services.AddScoped<IDatabaseMigrator>(sp =>
            new PostgresDatabaseMigrator(sp.GetRequiredService<GroundUpDbContext>()));

        // Framework repositories over the application's context (factory lambdas pass the concrete
        // context, so they never resolve an unrelated DbContext such as a module's own).
        services.AddScoped<IBootstrapStateRepository>(sp =>
            new BootstrapStateRepository(sp.GetRequiredService<GroundUpDbContext>()));
        services.AddScoped<ISetupTransactionLogRepository>(sp =>
            new SetupTransactionLogRepository(
                sp.GetRequiredService<GroundUpDbContext>(),
                sp.GetRequiredService<ILogger<SetupTransactionLogRepository>>()));

        // Register DataSeederRunner as hosted service
        services.AddHostedService<DataSeederRunner>();

        return services;
    }
}
