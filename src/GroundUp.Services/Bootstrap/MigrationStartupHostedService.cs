using GroundUp.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GroundUp.Services.Bootstrap;

/// <summary>
/// Runs database migrations during host startup BEFORE the application accepts traffic.
/// Registered as IHostedService; StartAsync awaits <see cref="IDatabaseMigrator.MigrateAsync"/> to completion.
/// The database provider (for example <c>AddGroundUpPostgres</c>) registers the migrator.
/// </summary>
public sealed class MigrationStartupHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationStartupHostedService> _logger;

    public MigrationStartupHostedService(IServiceProvider serviceProvider, ILogger<MigrationStartupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying database migrations...");
        using var scope = _serviceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(cancellationToken);
        _logger.LogInformation("Database migrations complete.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
