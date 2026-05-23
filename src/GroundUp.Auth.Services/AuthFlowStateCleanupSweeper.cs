using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Background service that periodically expires stale Pending AuthFlowState rows
/// and prunes terminal rows past the retention window.
/// Multi-instance safe: bulk SQL operations are inherently atomic per-row.
/// </summary>
public sealed class AuthFlowStateCleanupSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuthOptions> _optionsMonitor;
    private readonly ILogger<AuthFlowStateCleanupSweeper> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthFlowStateCleanupSweeper"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes per sweep cycle.</param>
    /// <param name="optionsMonitor">Auth options monitor for live-reloadable sweep settings.</param>
    /// <param name="logger">Logger for structured sweep reporting.</param>
    public AuthFlowStateCleanupSweeper(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuthOptions> optionsMonitor,
        ILogger<AuthFlowStateCleanupSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.CleanupIntervalMinutes);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IAuthFlowStateRepository>();

                // Step 1: Expire stale Pending rows
                var expiredResult = await repository.MarkExpiredOlderThanAsync(
                    DateTime.UtcNow, stoppingToken);
                var expiredCount = expiredResult.Success ? expiredResult.Data : 0;

                // Step 2: Delete terminal rows past retention
                var retentionDays = _optionsMonitor.CurrentValue.RetentionDays;
                var retentionCutoff = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);
                var deletedResult = await repository.DeleteTerminalOlderThanAsync(
                    retentionCutoff, stoppingToken);
                var deletedCount = deletedResult.Success ? deletedResult.Data : 0;

                if (expiredCount > 0)
                {
                    _logger.LogInformation("AuthFlowState sweep: expired {Count} rows", expiredCount);
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation("AuthFlowState sweep: deleted {Count} terminal rows", deletedCount);
                }

                if (expiredCount == 0 && deletedCount == 0)
                {
                    _logger.LogDebug("AuthFlowState sweep: no work");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuthFlowState sweep cycle failed");
            }
        }
    }
}
