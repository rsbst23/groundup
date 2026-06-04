using GroundUp.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GroundUp.Services.Bootstrap;

/// <summary>
/// Validates that GroundUp:BootstrapAdminToken is configured when setup is incomplete.
/// Runs AFTER migrations (registration order matters). Throws to fail host startup
/// if the token is missing in setup mode.
/// </summary>
public sealed class BootstrapTokenStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public BootstrapTokenStartupValidator(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapStateService>();
        var isComplete = await bootstrap.IsCompleteAsync(cancellationToken);

        if (isComplete) return; // Req 8.9: post-setup, token is not required

        var token = _configuration["GroundUp:BootstrapAdminToken"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "GroundUp:BootstrapAdminToken must be configured when setup is incomplete.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
