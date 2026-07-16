using FluentValidation;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Bootstrap;
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
        AddOptionsValidation(services);

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
        AddOptionsValidation(services);

        RegisterCoreServices(services);

        return services;
    }

    /// <summary>
    /// Registers data-annotation–free validation rules for <see cref="AuthOptions"/>.
    /// Validation fires during host startup via <c>ValidateOnStart</c>, so missing or
    /// undersized signing keys fail fast at boot rather than on the first request.
    /// </summary>
    private static void AddOptionsValidation(IServiceCollection services)
    {
        services.AddOptions<AuthOptions>()
            .Validate(options =>
            {
                if (string.IsNullOrWhiteSpace(options.JwtSigningKey))
                {
                    return false;
                }

                // HMAC-SHA256 requires at least 256 bits per RFC 4868
                var keyByteCount = System.Text.Encoding.UTF8.GetByteCount(options.JwtSigningKey);
                return keyByteCount >= ConfigurationSigningKeyProvider.MinimumKeyBytes;
            },
            $"AuthOptions.JwtSigningKey must be configured and at least {ConfigurationSigningKeyProvider.MinimumKeyBytes} bytes ({ConfigurationSigningKeyProvider.MinimumKeyBytes * 8} bits) when UTF-8 encoded for HMAC-SHA256.")
            .Validate(options => options.AbsoluteSessionLifetimeMinutes > 0,
                "AuthOptions.AbsoluteSessionLifetimeMinutes must be greater than 0.")
            .Validate(options => options.AbsoluteSessionLifetimeMinutes >= options.TokenExpirationMinutes,
                "AuthOptions.AbsoluteSessionLifetimeMinutes must be greater than or equal to TokenExpirationMinutes.")
            .Validate(options => options.CleanupIntervalMinutes > 0,
                "AuthOptions.CleanupIntervalMinutes must be greater than 0.")
            .Validate(options => options.RetentionDays >= 0,
                "AuthOptions.RetentionDays must be 0 or greater.")
            .ValidateOnStart();
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

        // JWT-based identity for HTTP scenarios.
        // ICurrentUser: registered here as JwtCurrentUser (claims-based).
        // ITenantContext: NOT registered here — it is owned by AddGroundUpApi() which registers
        //   the concrete TenantContext + ITenantContext alias. The JwtTenantResolutionMiddleware
        //   hydrates the concrete TenantContext from the JWT 'tid' claim, so repositories
        //   reading ITenantContext see the same instance. SDK-only consumers (no HTTP) can
        //   register JwtTenantContext or SystemTenantContext explicitly.
        services.AddScoped<ICurrentUser, JwtCurrentUser>();

        // Cache invalidation event handlers
        services.AddScoped<IEventHandler<EntityCreatedEvent<UserRoleDto>>, UserRoleChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<UserRoleDto>>, UserRoleChangedHandler>();
        services.AddScoped<IEventHandler<EntityCreatedEvent<RolePolicyDto>>, RolePolicyChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<RolePolicyDto>>, RolePolicyChangedHandler>();
        services.AddScoped<IEventHandler<EntityCreatedEvent<PolicyPermissionDto>>, PolicyPermissionChangedHandler>();
        services.AddScoped<IEventHandler<EntityDeletedEvent<PolicyPermissionDto>>, PolicyPermissionChangedHandler>();

        // AuthFlowState services
        services.AddScoped<IAuthFlowStateService, AuthFlowStateService>();
        services.AddScoped<IValidator<InitiateAuthFlowRequest>, InitiateAuthFlowRequestValidator>();

        // Bootstrap services
        services.AddScoped<IIdentityBootstrapService, IdentityBootstrapService>();

        // Auth dispatcher services (Phase 10C)
        services.AddScoped<IAuthCookieWriter, AuthCookieWriter>();
        services.AddScoped<IHostTenantResolver, HostTenantResolver>();
        services.AddScoped<HostResolvedTenant>();
        services.AddScoped<IAuthUrlBuilder, AuthUrlBuilderService>();
        services.AddScoped<IAuthFlowService, AuthFlowService>();
        services.AddScoped<IFlowHandler, NewOrganizationFlowHandler>();
        services.AddScoped<IFlowHandler, LoginFlowHandler>();
        services.AddScoped<LastAdminGuard>();

        // Cleanup sweeper (hosted service)
        services.AddHostedService<AuthFlowStateCleanupSweeper>();
    }
}
