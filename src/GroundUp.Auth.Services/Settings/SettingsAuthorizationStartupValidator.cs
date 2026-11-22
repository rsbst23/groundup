using System.Reflection;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GroundUp.Auth.Services.Settings;

/// <summary>
/// Stops the application at startup when the settings services are not wrapped by the
/// authorization proxy. The attributes on <see cref="ISettingsService"/> and
/// <see cref="ISettingsAdminService"/> (listing reads and admin writes) only take effect through
/// <c>AddAuthorized</c>. Without it those methods would be open, with no error.
/// <para>
/// The check applies in every environment. It is skipped when the settings module is not
/// registered at all.
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

        Check<ISettingsService>(provider);
        Check<ISettingsAdminService>(provider);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Check<TService>(IServiceProvider provider)
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
            $"attributes are not enforced, so listing reads and admin writes would be open to every caller. " +
            $"Register it with services.AddAuthorized<{name}, {name[1..]}>() (from GroundUp.Auth.Services.Authorization) " +
            $"after AddGroundUpSettings().");
    }
}
