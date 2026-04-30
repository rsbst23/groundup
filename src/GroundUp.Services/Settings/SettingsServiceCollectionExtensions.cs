using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Services.Settings;

/// <summary>
/// Extension methods for registering the GroundUp settings service
/// in the dependency injection container.
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISettingsService"/> as <see cref="SettingsService"/>
    /// with scoped lifetime. Does not require <see cref="ISettingEncryptionProvider"/>
    /// to be registered — the settings service works without encryption support,
    /// and only fails when an encrypted setting is actually accessed without a provider.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpSettings(this IServiceCollection services)
    {
        services.AddScoped<ISettingsService, SettingsService>();
        return services;
    }
}
