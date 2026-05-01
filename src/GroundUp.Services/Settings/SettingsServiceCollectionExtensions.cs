using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Services.Settings;

/// <summary>
/// Extension methods for registering the GroundUp settings module
/// in the dependency injection container.
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// Registers all settings module services: <see cref="ISettingsService"/>,
    /// <see cref="IScopeChainProvider"/>, <see cref="ISettingsAdminService"/>,
    /// in-memory cache, cache invalidation handler, and <see cref="SettingsCacheKeyTracker"/>.
    /// <para>
    /// The <see cref="IScopeChainProvider"/> is registered with <c>TryAddScoped</c>,
    /// allowing consuming applications to override it by registering their own
    /// implementation before or after calling this method.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configureCacheOptions">
    /// Optional action to configure <see cref="SettingsCacheOptions"/> (e.g., cache TTL).
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpSettings(
        this IServiceCollection services,
        Action<SettingsCacheOptions>? configureCacheOptions = null)
    {
        // Phase 6B: resolution service
        services.AddScoped<ISettingsService, SettingsService>();

        // Phase 6C: scope chain provider (overridable via TryAddScoped)
        services.TryAddScoped<IScopeChainProvider, DefaultScopeChainProvider>();

        // Phase 6C: admin service
        services.AddScoped<ISettingsAdminService, SettingsAdminService>();

        // Phase 6C: caching
        services.AddMemoryCache();
        services.Configure<SettingsCacheOptions>(options =>
        {
            configureCacheOptions?.Invoke(options);
        });

        // Phase 6C: cache key tracker (singleton — shared between SettingsService and handler)
        services.TryAddSingleton<SettingsCacheKeyTracker>();

        // Phase 6C: cache invalidation event handler
        services.AddScoped<IEventHandler<SettingChangedEvent>, SettingsCacheInvalidationHandler>();

        return services;
    }
}
