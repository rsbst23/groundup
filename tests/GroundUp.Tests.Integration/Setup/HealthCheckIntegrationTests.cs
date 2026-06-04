using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Api.HealthChecks;
using GroundUp.Api.Middleware;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// Integration tests for the /ready health check endpoint in setup mode.
/// Verifies that /ready returns { status: "Healthy", setupMode: true } when
/// the application is in bootstrap/setup mode.
/// </summary>
[Collection("HealthCheckApi")]
public sealed class HealthCheckIntegrationTests : IAsyncLifetime
{
    private readonly HealthCheckApiFactory _factory;
    private HttpClient _client = null!;

    public HealthCheckIntegrationTests(HealthCheckApiFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Ready_InSetupMode_ReturnsHealthyWithSetupModeTrue()
    {
        // Arrange
        HealthCheckApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("setupMode").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ready_AfterSetupComplete_ReturnsHealthyWithSetupModeFalse()
    {
        // Arrange
        HealthCheckApiFactory.IsSetupComplete = true;

        // Act
        var response = await _client.GetAsync("/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("Healthy");
        root.GetProperty("setupMode").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Ready_InSetupMode_IsNotRedirectedByBootstrapMiddleware()
    {
        // Arrange
        HealthCheckApiFactory.IsSetupComplete = false;

        // Act
        var response = await _client.GetAsync("/ready");

        // Assert — /ready should pass through the bootstrap middleware, not be redirected
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// WebApplicationFactory for health check integration tests.
/// Maps the /ready endpoint with a custom response writer that includes setupMode,
/// registers the BootstrapModeMiddleware, and provides a controllable
/// <see cref="IBootstrapStateService"/> implementation.
/// </summary>
public sealed class HealthCheckApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
    /// <summary>
    /// Controls whether the bootstrap state reports setup as complete.
    /// </summary>
    public static volatile bool IsSetupComplete = false;

    /// <summary>
    /// A deterministic 32-byte test master key for health check validation.
    /// </summary>
    private static readonly byte[] TestMasterKey = new byte[32];

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
        services.AddScoped<IBootstrapStateService, HealthCheckTestBootstrapStateService>();

        // Register IMasterKeyProvider with a valid test key so MasterKeyHealthCheck passes
        services.RemoveAll<IMasterKeyProvider>();
        services.AddSingleton<IMasterKeyProvider>(new HealthCheckTestMasterKeyProvider(TestMasterKey));

        // Remove all existing health check registrations (including the Sample app's NpgSql check)
        // and register only the MasterKeyHealthCheck for a controlled test environment
        services.RemoveAll<IHealthCheckPublisher>();

        // Clear and re-register health checks with only our controlled check
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            options.Registrations.Clear();
            options.Registrations.Add(new HealthCheckRegistration(
                "master-key",
                sp => new MasterKeyHealthCheck(sp.GetRequiredService<IMasterKeyProvider>()),
                failureStatus: null,
                tags: new[] { "infrastructure" }));
        });

        // Register a startup filter that inserts the BootstrapModeMiddleware and maps /ready
        services.AddSingleton<IStartupFilter, HealthCheckStartupFilter>();
    }
}

/// <summary>
/// Startup filter that inserts <see cref="BootstrapModeMiddleware"/> and a custom
/// /ready endpoint that returns health status with setupMode flag.
/// </summary>
internal sealed class HealthCheckStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<BootstrapModeMiddleware>();

            // Map /ready as inline middleware before the rest of the pipeline.
            // This simulates the MapHealthChecks("/ready", ...) with custom ResponseWriter
            // that the consuming application would configure per the design doc.
            app.Map("/ready", readyApp =>
            {
                readyApp.Run(async ctx =>
                {
                    var healthService = ctx.RequestServices.GetRequiredService<HealthCheckService>();
                    var report = await healthService.CheckHealthAsync(ctx.RequestAborted);

                    var bootstrap = ctx.RequestServices.GetRequiredService<IBootstrapStateService>();
                    var isComplete = await bootstrap.IsCompleteAsync(ctx.RequestAborted);

                    ctx.Response.ContentType = "application/json";
                    ctx.Response.StatusCode = report.Status == HealthStatus.Healthy
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status503ServiceUnavailable;

                    var body = new
                    {
                        status = report.Status.ToString(),
                        setupMode = !isComplete
                    };
                    await ctx.Response.WriteAsync(
                        JsonSerializer.Serialize(body, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));
                });
            });

            next(app);
        };
    }
}

/// <summary>
/// Test implementation of <see cref="IBootstrapStateService"/> for health check tests.
/// </summary>
internal sealed class HealthCheckTestBootstrapStateService : IBootstrapStateService
{
    public Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckApiFactory.IsSetupComplete);

    public Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        HealthCheckApiFactory.IsSetupComplete = true;
        return Task.FromResult(OperationResult.Ok());
    }

    public void InvalidateCache() { }
}

/// <summary>
/// Test master key provider that returns a valid 32-byte key.
/// </summary>
internal sealed class HealthCheckTestMasterKeyProvider : IMasterKeyProvider
{
    private readonly byte[] _key;

    public HealthCheckTestMasterKeyProvider(byte[] key) => _key = key;

    public byte[] GetKey() => _key;
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="HealthCheckApiFactory"/>
/// across all health check integration test classes.
/// </summary>
[CollectionDefinition("HealthCheckApi")]
public sealed class HealthCheckApiCollection : ICollectionFixture<HealthCheckApiFactory>
{
}
