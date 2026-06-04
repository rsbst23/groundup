namespace GroundUp.Api.HealthChecks;

using GroundUp.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Verifies that IMasterKeyProvider.GetKey() succeeds and returns >= 32 bytes.
/// </summary>
public sealed class MasterKeyHealthCheck : IHealthCheck
{
    private readonly IMasterKeyProvider _provider;

    public MasterKeyHealthCheck(IMasterKeyProvider provider) => _provider = provider;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = _provider.GetKey();
            return Task.FromResult(key.Length >= 32
                ? HealthCheckResult.Healthy("Master key resolved successfully.")
                : HealthCheckResult.Unhealthy($"Master key length {key.Length} < 32."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Master key unavailable.", ex));
        }
    }
}
