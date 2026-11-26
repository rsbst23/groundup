using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            .AddPolicyHandler((sp, _) => KeycloakRetryPolicy.Create(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("GroundUp.Auth.Keycloak.HttpRetry")));

        services.AddHttpClient("KeycloakAdmin", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler((sp, _) => KeycloakRetryPolicy.Create(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("GroundUp.Auth.Keycloak.HttpRetry")));

        // Singleton caches
        services.AddSingleton<AdminTokenCache>();
        services.AddSingleton<JwksCache>();

        // Scoped service implementations
        services.AddScoped<IIdentityProviderService, KeycloakIdentityProviderService>();
        services.AddScoped<IIdTokenValidator, KeycloakIdTokenValidator>();
        services.AddScoped<IIdentityProviderAdminService, KeycloakIdentityProviderAdminService>();

        // Singleton link builder
        services.AddSingleton<KeycloakAdminLinkBuilder>();

        return services;
    }
}
