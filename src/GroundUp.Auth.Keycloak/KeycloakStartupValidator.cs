using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Validates that all required Keycloak settings are configured at application startup.
/// Throws <see cref="InvalidOperationException"/> naming missing or invalid setting keys
/// to fail fast on misconfiguration.
/// </summary>
/// <remarks>
/// When <c>BootstrapState.IsComplete</c> is <c>false</c> and all settings are empty
/// (application is in setup mode), validation is skipped. If settings are partially
/// populated during setup mode, validation runs normally.
/// </remarks>
internal sealed class KeycloakStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="KeycloakStartupValidator"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for creating DI scopes.</param>
    public KeycloakStartupValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var bootstrapStateService = scope.ServiceProvider.GetService<IBootstrapStateService>();

        // If IBootstrapStateService is not registered, we can't determine bootstrap state.
        // In that case, skip validation when settings are not fully configured (dev/sample mode).
        bool isBootstrapComplete;
        if (bootstrapStateService is null)
        {
            isBootstrapComplete = false; // Treat as "not complete" so we apply the lenient check below
        }
        else
        {
            isBootstrapComplete = await bootstrapStateService.IsCompleteAsync(cancellationToken);
        }

        var optionsMonitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<KeycloakOptions>>();
        var options = optionsMonitor.CurrentValue;

        // If bootstrap is not complete (or unknown), skip validation when settings are missing.
        // This covers setup mode and dev scenarios where Keycloak isn't fully configured yet.
        if (!isBootstrapComplete && !AllSettingsPopulated(options))
        {
            return;
        }

        Validate(options);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool AllSettingsEmpty(KeycloakOptions options)
    {
        return string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            && string.IsNullOrWhiteSpace(options.SharedRealmName)
            && string.IsNullOrWhiteSpace(options.InternalBaseUrl)
            && string.IsNullOrWhiteSpace(options.AdminClientId)
            && string.IsNullOrWhiteSpace(options.AdminClientSecret)
            && string.IsNullOrWhiteSpace(options.AppClientId);
    }

    private static bool AllSettingsPopulated(KeycloakOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            && !string.IsNullOrWhiteSpace(options.SharedRealmName)
            && !string.IsNullOrWhiteSpace(options.InternalBaseUrl)
            && !string.IsNullOrWhiteSpace(options.AdminClientId)
            && !string.IsNullOrWhiteSpace(options.AdminClientSecret)
            && !string.IsNullOrWhiteSpace(options.AppClientId);
    }

    private static void Validate(KeycloakOptions options)
    {
        var missingKeys = new List<string>();
        var invalidUrls = new List<string>();

        ValidateRequired(options.PublicBaseUrl, KeycloakOptionsSetup.SettingKeys.PublicBaseUrl, missingKeys);
        ValidateRequired(options.SharedRealmName, KeycloakOptionsSetup.SettingKeys.SharedRealmName, missingKeys);
        ValidateRequired(options.InternalBaseUrl, KeycloakOptionsSetup.SettingKeys.InternalBaseUrl, missingKeys);
        ValidateRequired(options.AdminClientId, KeycloakOptionsSetup.SettingKeys.AdminClientId, missingKeys);
        ValidateRequired(options.AdminClientSecret, KeycloakOptionsSetup.SettingKeys.AdminClientSecret, missingKeys);
        ValidateRequired(options.AppClientId, KeycloakOptionsSetup.SettingKeys.AppClientId, missingKeys);

        // Validate URL format for PublicBaseUrl (only if it's not already flagged as missing)
        if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            && !IsValidAbsoluteHttpUrl(options.PublicBaseUrl))
        {
            invalidUrls.Add(KeycloakOptionsSetup.SettingKeys.PublicBaseUrl);
        }

        // Validate URL format for InternalBaseUrl (only if it's not already flagged as missing)
        if (!string.IsNullOrWhiteSpace(options.InternalBaseUrl)
            && !IsValidAbsoluteHttpUrl(options.InternalBaseUrl))
        {
            invalidUrls.Add(KeycloakOptionsSetup.SettingKeys.InternalBaseUrl);
        }

        if (missingKeys.Count > 0 || invalidUrls.Count > 0)
        {
            var messages = new List<string>();

            if (missingKeys.Count > 0)
            {
                messages.Add($"Missing or empty Keycloak settings: {string.Join(", ", missingKeys)}");
            }

            if (invalidUrls.Count > 0)
            {
                messages.Add($"Invalid URL format (must be absolute http/https URI): {string.Join(", ", invalidUrls)}");
            }

            throw new InvalidOperationException(
                $"Keycloak configuration validation failed. {string.Join(" ", messages)}");
        }
    }

    private static void ValidateRequired(string value, string key, List<string> missingKeys)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missingKeys.Add(key);
        }
    }

    private static bool IsValidAbsoluteHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
