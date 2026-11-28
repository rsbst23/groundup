using FluentValidation;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Auth.Services.Bootstrap;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.EventHandlers;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Auth.Services.JoinLinks;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Revocation;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Session;
using GroundUp.Auth.Services.Settings;
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
        // Per-tenant versions of cached permission sets; policy changes bump them (GU-96)
        services.AddSingleton<PermissionCacheVersions>();

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
        services.AddScoped<IValidator<CreateOrganizationRequestDto>, CreateOrganizationRequestDtoValidator>();

        // Bootstrap services
        services.AddScoped<IIdentityBootstrapService, IdentityBootstrapService>();

        // Session timeouts from settings, with the tenant-not-above-System rule on write
        services.AddScoped<ISessionPolicyProvider, SessionPolicyProvider>();
        services.AddScoped<ISettingWriteValidator, SessionPolicyWriteValidator>();
        services.AddScoped<CallbackSessionGuard>();

        // Settings authorization. Replaces the allow-all default registered by AddGroundUpSettings(),
        // whatever the call order. An application or module that wants its own authorizer registers
        // it after this call.
        services.Replace(ServiceDescriptor.Scoped<ISettingsAuthorizer, SettingsAuthorizer>());

        // Auth dispatcher services (Phase 10C)
        services.AddScoped<IAuthCookieWriter, AuthCookieWriter>();
        services.AddScoped<IHostTenantResolver, HostTenantResolver>();
        services.AddScoped<HostResolvedTenant>();
        services.AddScoped<IAuthUrlBuilder, AuthUrlBuilderService>();
        services.AddScoped<IAuthFlowService, AuthFlowService>();
        services.AddScoped<IFlowHandler, NewOrganizationFlowHandler>();
        services.AddScoped<IFlowHandler, LoginFlowHandler>();
        services.AddScoped<IFlowHandler, SimpleSignupFlowHandler>();
        services.AddScoped<LastAdminGuard>();
        services.AddScoped<TenantAccessRevoker>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IOrganizationProvisioningService, OrganizationProvisioningService>();
        // Role assignment (GU-68): one class behind the public, rule-checked IUserRoleService and the
        // internal system path IRoleGrantWriter. Applications add rules as IRoleAssignmentRule.
        services.AddScoped<RoleGrantPolicy>();
        // The escalation guard (GU-96): callers only grant permissions they hold
        services.AddScoped<PermissionHoldGuard>();
        services.AddScoped<UserRoleService>();
        services.AddScoped<IUserRoleService>(sp => sp.GetRequiredService<UserRoleService>());
        services.AddScoped<IRoleGrantWriter>(sp => sp.GetRequiredService<UserRoleService>());
        services.AddScoped<DefaultRoleAssigner>();
        // One resolver for every sign-in flow: finds the user and claims pending users
        services.AddScoped<SignInUserResolver>();
        // Member administration, behind the authorization proxy (permissions on the interface)
        services.AddAuthorized<IMemberManagementService, MemberManagementService>();
        // Role administration (GU-95), behind the authorization proxy
        services.AddScoped<IValidator<CreateRoleDto>, CreateRoleDtoValidator>();
        services.AddScoped<IValidator<UpdateRoleDto>, UpdateRoleDtoValidator>();
        services.AddAuthorized<IRoleService, RoleService>();
        // Invitations (GU-68). The logging email sender is a placeholder until Notifications (GU-64);
        // an application replaces it by registering its own IInvitationEmailSender.
        services.AddScoped<IValidator<CreateInvitationDto>, CreateInvitationDtoValidator>();
        services.TryAddScoped<IInvitationEmailSender, LoggingInvitationEmailSender>();
        services.AddAuthorized<IInvitationService, InvitationService>();
        // Accepting invitations: the public start (the token is the credential) and the callback
        services.AddScoped<IInvitationAcceptanceService, InvitationAcceptanceService>();
        services.AddScoped<IFlowHandler, InvitationFlowHandler>();
        // Join links (GU-68): admin service behind the proxy, public start, callback
        services.AddScoped<IValidator<CreateJoinLinkDto>, CreateJoinLinkDtoValidator>();
        services.AddAuthorized<IJoinLinkService, JoinLinkService>();
        services.AddScoped<IJoinLinkAcceptanceService, JoinLinkAcceptanceService>();
        services.AddScoped<IFlowHandler, JoinLinkFlowHandler>();

        // Token revocation (GU-71). In-memory and per process until the caching module (Phase 15, GU-47).
        // TimeProvider.System on purpose: the cutoff must use the clock that stamps iat on tokens, not
        // whatever TimeProvider the container holds. TryAdd so an application can register a shared store.
        services.TryAddSingleton<ITokenRevocationStore>(_ => new InMemoryTokenRevocationStore(TimeProvider.System));
        services.AddHostedService<TokenRevocationStartupValidator>();

        // Startup checks on cookie settings (CookieSecure outside Development, single-label domain)
        services.AddHostedService<AuthCookieStartupValidator>();

        // Stops startup when the settings services or the auth admin services are not wrapped by AddAuthorized
        services.AddHostedService<SettingsAuthorizationStartupValidator>();

        // Cleanup sweeper (hosted service)
        services.AddHostedService<AuthFlowStateCleanupSweeper>();
    }
}
