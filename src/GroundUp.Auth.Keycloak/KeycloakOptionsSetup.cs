using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Configures <see cref="KeycloakOptions"/> from the settings database via <see cref="ISettingsService"/>
/// and provides a change token that invalidates when any Keycloak setting changes.
/// </summary>
/// <remarks>
/// Registered as a singleton. Creates DI scopes to resolve the scoped <see cref="ISettingsService"/>.
/// Also implements <see cref="IEventHandler{SettingChangedEvent}"/> to detect setting changes
/// and trigger options refresh via the change token.
/// </remarks>
public sealed class KeycloakOptionsSetup
    : IConfigureOptions<KeycloakOptions>,
      IOptionsChangeTokenSource<KeycloakOptions>,
      IEventHandler<SettingChangedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource _changeTokenSource = new();

    private static readonly HashSet<string> KeycloakSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        SettingKeys.PublicBaseUrl,
        SettingKeys.SharedRealmName,
        SettingKeys.InternalBaseUrl,
        SettingKeys.AdminClientId,
        SettingKeys.AdminClientSecret,
        SettingKeys.AppClientId
    };

    /// <summary>
    /// Initializes a new instance of <see cref="KeycloakOptionsSetup"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for creating DI scopes.</param>
    public KeycloakOptionsSetup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => Options.DefaultName;

    /// <inheritdoc />
    public void Configure(KeycloakOptions options)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        options.PublicBaseUrl = GetSettingValue(settingsService, SettingKeys.PublicBaseUrl);
        options.SharedRealmName = GetSettingValue(settingsService, SettingKeys.SharedRealmName);
        options.InternalBaseUrl = GetSettingValue(settingsService, SettingKeys.InternalBaseUrl);
        options.AdminClientId = GetSettingValue(settingsService, SettingKeys.AdminClientId);
        options.AdminClientSecret = GetSettingValue(settingsService, SettingKeys.AdminClientSecret);
        options.AppClientId = GetSettingValue(settingsService, SettingKeys.AppClientId);
    }

    /// <inheritdoc />
    public IChangeToken GetChangeToken()
    {
        return new CancellationChangeToken(_changeTokenSource.Token);
    }

    /// <inheritdoc />
    public Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        if (KeycloakSettingKeys.Contains(@event.SettingKey))
        {
            SignalChange();
        }

        return Task.CompletedTask;
    }

    private void SignalChange()
    {
        var previousSource = Interlocked.Exchange(
            ref _changeTokenSource,
            new CancellationTokenSource());

        previousSource.Cancel();
        previousSource.Dispose();
    }

    private static string GetSettingValue(ISettingsService settingsService, string key)
    {
        // Use the convenience overload that resolves the scope chain internally.
        // During startup the scope chain may resolve to system-level only, which is correct.
        var result = settingsService.GetAsync<string>(key).GetAwaiter().GetResult();
        return result.Success ? result.Data ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Well-known setting keys for Keycloak configuration.
    /// </summary>
    internal static class SettingKeys
    {
        public const string PublicBaseUrl = "auth.keycloak.public-base-url";
        public const string SharedRealmName = "auth.keycloak.shared-realm-name";
        public const string InternalBaseUrl = "auth.keycloak.internal-base-url";
        public const string AdminClientId = "auth.keycloak.admin-client-id";
        public const string AdminClientSecret = "auth.keycloak.admin-client-secret";
        public const string AppClientId = "auth.keycloak.app-client-id";
    }
}
