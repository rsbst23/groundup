using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// WebApplicationFactory for settings integration tests. Uses the Sample app
/// as the entry point with Testcontainers Postgres. Registers a
/// <see cref="TestScopeChainProvider"/> that returns a configurable scope chain
/// for testing cascading resolution. Disables settings caching so that each
/// read goes to the database (avoids stale cache issues in tests).
/// </summary>
public sealed class SettingsApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
    /// <summary>
    /// The scope chain that will be returned by the test scope chain provider.
    /// Tests can set this before making requests to simulate different scope contexts.
    /// </summary>
    public static List<SettingScopeEntry> TestScopeChain { get; set; } = new();

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Replace the default scope chain provider with a test-controllable one
        services.RemoveAll<IScopeChainProvider>();
        services.AddScoped<IScopeChainProvider, TestScopeChainProvider>();

        // Disable caching for integration tests to avoid stale cache issues
        // (the event-based invalidation requires scoped handler resolution which
        // doesn't work with the singleton event bus in test environments)
        services.Configure<SettingsCacheOptions>(options =>
        {
            options.CacheDuration = TimeSpan.Zero;
        });
    }
}

/// <summary>
/// Test scope chain provider that returns the scope chain configured on
/// <see cref="SettingsApiFactory.TestScopeChain"/>. This allows integration tests
/// to control the scope chain without needing a real tenant context.
/// </summary>
internal sealed class TestScopeChainProvider : IScopeChainProvider
{
    public Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SettingScopeEntry> chain = SettingsApiFactory.TestScopeChain;
        return Task.FromResult(chain);
    }
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="SettingsApiFactory"/>
/// across all settings integration test classes.
/// </summary>
[CollectionDefinition("SettingsApi")]
public sealed class SettingsApiCollection : ICollectionFixture<SettingsApiFactory>
{
}
