using GroundUp.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Hosted service that discovers all <see cref="IDataSeeder"/> implementations
/// from DI, orders them by <see cref="IDataSeeder.Order"/>, and runs
/// <see cref="IDataSeeder.SeedAsync"/> on each during application startup.
/// Errors in individual seeders are logged but do not prevent other seeders from running.
/// </summary>
public sealed class DataSeederRunner : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSeederRunner> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DataSeederRunner"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for creating scopes.</param>
    /// <param name="logger">Logger for recording seeder execution and failures.</param>
    public DataSeederRunner(IServiceProvider serviceProvider, ILogger<DataSeederRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Discovers and runs all registered <see cref="IDataSeeder"/> implementations
    /// in ascending <see cref="IDataSeeder.Order"/>.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var seeders = scope.ServiceProvider
            .GetServices<IDataSeeder>()
            .OrderBy(s => s.Order);

        foreach (var seeder in seeders)
        {
            try
            {
                _logger.LogInformation(
                    "Running data seeder {SeederType} (Order: {Order})",
                    seeder.GetType().Name,
                    seeder.Order);

                await seeder.SeedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Data seeder {SeederType} failed",
                    seeder.GetType().Name);
            }
        }
    }

    /// <summary>
    /// No cleanup required on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
