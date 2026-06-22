using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services;
using GroundUp.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers all GroundUp.Auth.Keycloak services into the dependency injection container.
/// Call <see cref="AddGroundUpAuthKeycloak"/> from the consuming application's startup.
/// </summary>
public static class KeycloakServiceCollectionExtensions
{
    /// <summary>
    /// Registers Keycloak identity provider services including options, HTTP clients with Polly,
    /// caches, and both identity provider service implementations.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpAuthKeycloak(this IServiceCollection services)
    {
        // Options setup — single instance used for both IConfigureOptions and IOptionsChangeTokenSource
        services.AddSingleton<KeycloakOptionsSetup>();
        services.AddSingleton<IConfigureOptions<KeycloakOptions>>(sp =>
            sp.GetRequiredService<KeycloakOptionsSetup>());
        services.AddSingleton<IOptionsChangeTokenSource<KeycloakOptions>>(sp =>
            sp.GetRequiredService<KeycloakOptionsSetup>());
        services.AddSingleton<IEventHandler<SettingChangedEvent>>(sp =>
            sp.GetRequiredService<KeycloakOptionsSetup>());

        // Startup validation
        services.AddHostedService<KeycloakStartupValidator>();

        // Named HTTP clients with Polly retry policy
        services.AddHttpClient("KeycloakIdp", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler((sp, _) => GetRetryPolicy(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("GroundUp.Auth.Keycloak.HttpRetry")));

        services.AddHttpClient("KeycloakAdmin", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler((sp, _) => GetRetryPolicy(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("GroundUp.Auth.Keycloak.HttpRetry")));

        // Singleton caches
        services.AddSingleton<AdminTokenCache>();
        services.AddSingleton<JwksCache>();

        // Scoped service implementations
        services.AddScoped<IIdentityProviderService, KeycloakIdentityProviderService>();
        services.AddScoped<IIdentityProviderAdminService, KeycloakIdentityProviderAdminService>();

        // Singleton link builder
        services.AddSingleton<KeycloakAdminLinkBuilder>();

        return services;
    }

    /// <summary>
    /// Creates the Polly retry policy for Keycloak HTTP calls.
    /// Retries on HTTP 5xx and transient network exceptions with exponential backoff and jitter.
    /// Logs each retry attempt at Warning level.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
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
}
