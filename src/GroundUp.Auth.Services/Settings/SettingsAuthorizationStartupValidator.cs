using System.Reflection;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Auth.Services.Membership;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GroundUp.Auth.Services.Settings;

/// <summary>
/// Stops the application at startup when a service whose permissions are declared with
/// <c>[RequiresPermission]</c> is not wrapped by the authorization proxy. The attributes only take
/// effect through <c>AddAuthorized</c>; without it those methods would be open, with no error.
/// <list type="bullet">
/// <item><see cref="ISettingsService"/> and <see cref="ISettingsAdminService"/>: the application
/// registers them after <c>AddGroundUpSettings()</c>.</item>
/// <item>The auth module's admin services (<see cref="IMemberManagementService"/>,
/// <see cref="IInvitationService"/>): registered by
/// <c>AddGroundUpAuth</c>; checked in case an application replaces the registration.</item>
/// </list>
/// <para>
/// The check applies in every environment. A service that is not registered at all is skipped.
/// </para>
/// </summary>
public sealed class SettingsAuthorizationStartupValidator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsAuthorizationStartupValidator"/>.
    /// </summary>
    /// <param name="scopeFactory">Creates a scope to resolve the settings services.</param>
    public SettingsAuthorizationStartupValidator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// A settings service is registered without the authorization proxy.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        Check<ISettingsService>(provider, "after AddGroundUpSettings()");
        Check<ISettingsAdminService>(provider, "after AddGroundUpSettings()");
        Check<IMemberManagementService>(provider, "or remove the replacement registration (AddGroundUpAuth registers it)");
        Check<IInvitationService>(provider, "or remove the replacement registration (AddGroundUpAuth registers it)");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Check<TService>(IServiceProvider provider, string hint)
        where TService : class
    {
        var service = provider.GetService<TService>();

        // Not registered: the settings module is not in use, nothing to protect.
        // A DispatchProxy instance is the authorization proxy created by AddAuthorized.
        if (service is null || service is DispatchProxy)
        {
            return;
        }

        var name = typeof(TService).Name;
        throw new InvalidOperationException(
            $"{name} is registered without authorization. Its [RequiresPermission] and [RequiresRole] " +
            $"attributes are not enforced, so its reads and admin writes would be open to every caller. " +
            $"Register it with services.AddAuthorized<{name}, {name[1..]}>() (from GroundUp.Auth.Services.Authorization) " +
            $"{hint}.");
    }
}
