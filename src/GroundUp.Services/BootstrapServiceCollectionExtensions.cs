namespace GroundUp.Services;

using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Services.Bootstrap;
using GroundUp.Services.Configuration;
using GroundUp.Services.Security;
using GroundUp.Services.Setup;
using GroundUp.Services.Setup.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>
/// Registers the bootstrap-mode primitives: master key provider, bootstrap state service,
/// options, validators, and startup hosted services.
/// </summary>
public static class BootstrapServiceCollectionExtensions
{
    public static IServiceCollection AddGroundUpBootstrap(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Master key provider (singleton)
        services.AddSingleton<IMasterKeyProvider, EnvironmentFileMasterKeyProvider>();

        // Bootstrap state service (scoped)
        services.AddScoped<IBootstrapStateService, BootstrapStateService>();

        // Setup wizard service (scoped — internal class, must be registered from same assembly)
        services.AddScoped<ISetupWizardService, SetupWizardService>();

        // Typed Keycloak admin HttpClient (30s timeout)
        services.AddHttpClient<KeycloakAdminHttpClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Memory cache (idempotent — may already be registered)
        services.AddMemoryCache();

        // Options binding + validation
        services.AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BootstrapOptions>, BootstrapOptionsValidator>();

        services.AddOptions<SetupTransactionLogOptions>()
            .Bind(configuration.GetSection(SetupTransactionLogOptions.SectionName));

        // AES-GCM as default ISettingEncryptionProvider (only if not already registered)
        services.TryAddSingleton<ISettingEncryptionProvider, AesGcmSettingEncryptionProvider>();

        // Hosted services — order matters: migrations BEFORE token validation
        services.AddHostedService<MigrationStartupHostedService>();
        services.AddHostedService<BootstrapTokenStartupValidator>();

        return services;
    }
}
