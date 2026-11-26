using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// The Polly retry policy for Keycloak HTTP calls. Retries HTTP 5xx, 408 and transient network
/// exceptions (<see cref="HttpPolicyExtensions.HandleTransientHttpError"/>) with exponential backoff
/// and jitter, and logs each retry at Warning level. 4xx responses other than 408 are not retried.
/// After the last retry the final response is returned (or the final exception rethrown).
/// </summary>
internal static class KeycloakRetryPolicy
{
    /// <summary>The number of retries after the first attempt.</summary>
    public const int RetryCount = 3;

    /// <summary>
    /// Creates the policy.
    /// </summary>
    /// <param name="logger">Logger for retry attempts.</param>
    /// <param name="sleepDurationProvider">
    /// Delay before each retry (1-based attempt number). Defaults to 1 s, 2 s, 4 s plus up to 1 s of
    /// jitter. Tests pass a zero delay.
    /// </param>
    public static IAsyncPolicy<HttpResponseMessage> Create(
        ILogger logger, Func<int, TimeSpan>? sleepDurationProvider = null)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: RetryCount,
                sleepDurationProvider: sleepDurationProvider ?? DefaultDelay,
                onRetry: (outcome, delay, attempt, _) =>
                {
                    if (outcome.Exception is not null)
                    {
                        logger.LogWarning(
                            outcome.Exception,
                            "Keycloak HTTP retry attempt {Attempt} after {Delay}ms due to exception",
                            attempt,
                            delay.TotalMilliseconds);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Keycloak HTTP retry attempt {Attempt} after {Delay}ms due to status code {StatusCode}",
                            attempt,
                            delay.TotalMilliseconds,
                            (int)outcome.Result.StatusCode);
                    }
                });
    }

    private static TimeSpan DefaultDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))
        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
}
