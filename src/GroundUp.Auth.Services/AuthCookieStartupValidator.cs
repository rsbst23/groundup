using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Startup checks on the auth cookie configuration.
/// <list type="bullet">
///   <item><see cref="AuthOptions.CookieSecure"/> = <c>false</c> outside the Development environment
///   stops the application: auth cookies would travel over plain HTTP. In Development it logs a warning.</item>
///   <item>A single-label <c>auth.application.default-domain</c> (for example <c>localhost</c>) logs a
///   warning: browsers drop a cookie whose Domain is a single label on subdomain hosts, so subdomain
///   tenants cannot sign in. Use a two-part domain such as <c>groundup.test</c>.</item>
/// </list>
/// </summary>
public sealed class AuthCookieStartupValidator : IHostedService
{
    private const string DefaultDomainSettingKey = "auth.application.default-domain";

    private readonly IOptions<AuthOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthCookieStartupValidator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthCookieStartupValidator"/>.
    /// </summary>
    /// <param name="options">Auth options holding the cookie settings.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <param name="scopeFactory">Creates a scope for reading settings.</param>
    /// <param name="logger">Logger for warnings.</param>
    public AuthCookieStartupValidator(
        IOptions<AuthOptions> options,
        IHostEnvironment environment,
        IServiceScopeFactory scopeFactory,
        ILogger<AuthCookieStartupValidator> logger)
    {
        _options = options;
        _environment = environment;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <see cref="AuthOptions.CookieSecure"/> is <c>false</c> outside the Development environment.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.CookieSecure)
        {
            if (!_environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"GroundUp:Auth:CookieSecure is false in the '{_environment.EnvironmentName}' environment. " +
                    "Auth cookies must be sent over HTTPS only. CookieSecure=false is allowed in Development only.");
            }

            _logger.LogWarning(
                "GroundUp:Auth:CookieSecure is false. Auth cookies can travel over plain HTTP. This is allowed in Development only.");
        }

        await WarnOnSingleLabelDomainAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// True when <paramref name="domain"/> is non-empty and has no dot (for example <c>localhost</c>).
    /// </summary>
    /// <param name="domain">The configured default domain.</param>
    public static bool IsSingleLabelDomain(string? domain)
    {
        var trimmed = domain?.Trim().Trim('.');
        return !string.IsNullOrEmpty(trimmed) && !trimmed.Contains('.', StringComparison.Ordinal);
    }

    private async Task WarnOnSingleLabelDomainAsync(CancellationToken cancellationToken)
    {
        string? domain;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetService<ISettingsService>();
            if (settings is null)
            {
                return;
            }

            var result = await settings.GetAsync<string>(DefaultDomainSettingKey, cancellationToken);
            domain = result.Success ? result.Data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database may not be ready yet (first run, setup mode). The check is advisory.
            _logger.LogDebug(ex, "Could not read {SettingKey} at startup; skipping the cookie domain check.", DefaultDomainSettingKey);
            return;
        }

        if (IsSingleLabelDomain(domain))
        {
            _logger.LogWarning(
                "auth.application.default-domain is the single-label domain '{Domain}'. Browsers drop the auth cookie on " +
                "subdomain hosts (for example tenant.{Domain}), so subdomain tenants cannot sign in. Use a domain with at " +
                "least two labels, such as groundup.test.",
                domain,
                domain);
        }
    }
}
