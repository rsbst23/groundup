using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.EventHandlers;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Token;
using GroundUp.Core.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Auth.Services;

/// <summary>
/// Extension methods for registering all GroundUp auth service layer components
/// in the dependency injection container.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers all auth service layer components: <see cref="IPermissionService"/>,
    /// <see cref="ICurrentUser"/> (JWT-based), <see cref="ITenantContext"/> (JWT-based),
    /// cache invalidation event handlers, and <see cref="AuthOptions"/> configuration.
    /// Binds <see cref="AuthOptions"/> from the "GroundUp:Auth" configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection("GroundUp:Auth"));

        RegisterCoreServices(services);

        return services;
    }

    /// <summary>
    /// Registers all auth service layer components with an explicit configuration action
    /// for <see cref="AuthOptions"/>. Use this overload when configuration is not file-based.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure <see cref="AuthOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpAuth(this IServiceCollection services, Action<AuthOptions> configure)
    {
        services.Configure(configure);

        RegisterCoreServices(services);

        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        // Permission service
        services.AddScoped<IPermissionService, PermissionService>();

        // Token and session services
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.TryAddScoped<ISigningKeyProvider, ConfigurationSigningKeyProvider>();

        // JWT-based identity (default for HTTP scenarios)
        services.AddScoped<ICurrentUser, JwtCurrentUser>();
        services.AddScoped<ITenantContext, JwtTenantContext>();

        // Cache invalidation event handlers
        services.AddScoped<IEventHandler<EntityCreatedEvent<UserRoleDto>>, UserRoleChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<UserRoleDto>>, UserRoleChangedHandler>();
        services.AddScoped<IEventHandler<EntityCreatedEvent<RolePolicyDto>>, RolePolicyChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<RolePolicyDto>>, RolePolicyChangedHandler>();
        services.AddScoped<IEventHandler<EntityCreatedEvent<PolicyPermissionDto>>, PolicyPermissionChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<PolicyPermissionDto>>, PolicyPermissionChangedHandler>();
    }
}
