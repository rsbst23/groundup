using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Revocation;

/// <summary>
/// Stops the application in the Production environment when the in-memory token revocation store is
/// in use, unless <see cref="AuthOptions.AllowInMemoryTokenRevocationStore"/> is set. The in-memory
/// store is per process, so with several instances a revoked token keeps working on the instances
/// that did not handle the revocation until it expires. When the option is set, startup logs a warning.
/// Other environments and other stores are not affected.
/// </summary>
public sealed class TokenRevocationStartupValidator : IHostedService
{
    private readonly ITokenRevocationStore _store;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<TokenRevocationStartupValidator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TokenRevocationStartupValidator"/>.
    /// </summary>
    /// <param name="store">The registered revocation store.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <param name="options">Auth options holding the opt-in flag.</param>
    /// <param name="logger">Logger for the opt-in warning.</param>
    public TokenRevocationStartupValidator(
        ITokenRevocationStore store,
        IHostEnvironment environment,
        IOptions<AuthOptions> options,
        ILogger<TokenRevocationStartupValidator> logger)
    {
        _store = store;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Production, in-memory store, and the opt-in option is not set.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsProduction() || _store is not InMemoryTokenRevocationStore)
        {
            return Task.CompletedTask;
        }

        const string limitation =
            "The in-memory token revocation store is per process: when a user leaves a tenant, only the " +
            "instance that handled it rejects their old token; other instances accept it until it expires " +
            "(at most the token lifetime). A shared store arrives with the caching module (Phase 15).";

        if (!_options.Value.AllowInMemoryTokenRevocationStore)
        {
            throw new InvalidOperationException(
                limitation + " Production refuses to start with this store. Register a shared " +
                "ITokenRevocationStore, or, for a single-instance deployment only, set " +
                "GroundUp:Auth:AllowInMemoryTokenRevocationStore to true.");
        }

        _logger.LogWarning(
            "{Limitation} Allowed by GroundUp:Auth:AllowInMemoryTokenRevocationStore; run a single instance.",
            limitation);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
