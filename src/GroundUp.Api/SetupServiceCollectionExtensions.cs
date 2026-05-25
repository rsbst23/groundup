namespace GroundUp.Api;

using GroundUp.Api.Authentication;
using GroundUp.Core.Configuration;
using GroundUp.Services.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Registers the setup wizard HTTP-side primitives: authentication scheme, options, and filters.
/// Depends on AddGroundUpBootstrap() being called first (which registers ISetupWizardService,
/// KeycloakAdminHttpClient, and other service-layer dependencies).
/// </summary>
public static class SetupServiceCollectionExtensions
{
    public static IServiceCollection AddGroundUpSetup(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Setup options
        services.AddOptions<SetupOptions>()
            .Bind(configuration.GetSection(SetupOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SetupOptions>, SetupOptionsValidator>();

        // Authentication scheme
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BootstrapAdminTokenAuthenticationHandler>(
                BootstrapAdminTokenAuthenticationHandler.SchemeName, _ => { });

        return services;
    }
}
