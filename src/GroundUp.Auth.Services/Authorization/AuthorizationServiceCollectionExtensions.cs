using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Auth.Services.Authorization;

/// <summary>
/// DI extension methods for registering services with authorization proxy wrapping.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a service with the DispatchProxy authorization wrapper.
    /// The proxy intercepts calls and enforces <c>[RequiresPermission]</c> and <c>[RequiresRole]</c> attributes
    /// declared on the interface methods.
    /// </summary>
    /// <typeparam name="TInterface">The service interface type.</typeparam>
    /// <typeparam name="TImplementation">The concrete service implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAuthorized<TInterface, TImplementation>(this IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddScoped<TImplementation>();
        services.AddScoped<TInterface>(sp =>
        {
            var target = sp.GetRequiredService<TImplementation>();
            var permissionService = sp.GetRequiredService<IPermissionService>();
            var currentUser = sp.GetRequiredService<ICurrentUser>();
            return AuthorizationInterceptor<TInterface>.Create(target, permissionService, currentUser);
        });

        return services;
    }
}
