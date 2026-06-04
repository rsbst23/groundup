using GroundUp.Api.Middleware;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// WebApplicationFactory for bootstrap mode middleware integration tests.
/// Registers the BootstrapModeMiddleware via an <see cref="IStartupFilter"/> and provides
/// a controllable <see cref="IBootstrapStateService"/> implementation so tests can toggle
/// between setup mode and normal mode.
/// </summary>
public sealed class BootstrapModeApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
    /// <summary>
    /// Controls whether the bootstrap state reports setup as complete.
    /// Set this before making requests to simulate setup mode (false) or normal mode (true).
    /// </summary>
    public static volatile bool IsSetupComplete = false;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide required configuration values that the Sample app validates on startup
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GroundUp:Auth:JwtSigningKey"] = "integration-test-signing-key-must-be-at-least-32-bytes-long"
            });
        });

        base.ConfigureWebHost(builder);
    }

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Replace IBootstrapStateService with a test-controllable implementation
        services.RemoveAll<IBootstrapStateService>();
        services.AddScoped<IBootstrapStateService, TestBootstrapStateService>();

        // Register a startup filter that inserts the BootstrapModeMiddleware early in the pipeline
        services.AddSingleton<IStartupFilter, BootstrapModeStartupFilter>();
    }
}

/// <summary>
/// Startup filter that inserts <see cref="BootstrapModeMiddleware"/> at the beginning
/// of the middleware pipeline for integration testing.
/// </summary>
internal sealed class BootstrapModeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<BootstrapModeMiddleware>();
            next(app);
        };
    }
}

/// <summary>
/// Test implementation of <see cref="IBootstrapStateService"/> that returns
/// the value of <see cref="BootstrapModeApiFactory.IsSetupComplete"/>.
/// </summary>
internal sealed class TestBootstrapStateService : IBootstrapStateService
{
    public Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BootstrapModeApiFactory.IsSetupComplete);
    }

    public Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        BootstrapModeApiFactory.IsSetupComplete = true;
        return Task.FromResult(OperationResult.Ok());
    }

    public void InvalidateCache()
    {
        // No-op for tests
    }
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="BootstrapModeApiFactory"/>
/// across all bootstrap mode integration test classes.
/// </summary>
[CollectionDefinition("BootstrapModeApi")]
public sealed class BootstrapModeApiCollection : ICollectionFixture<BootstrapModeApiFactory>
{
}
