using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using GroundUp.Api.Authentication;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroundUp.Tests.Integration.Setup;

/// <summary>
/// WebApplicationFactory for setup rate limit integration tests.
/// Configures ASP.NET Core rate limiting with a global limiter scoped to /setup paths
/// and a controllable <see cref="IBootstrapStateService"/>.
/// Uses X-Test-RemoteIp header to simulate remote IP addresses since WebApplicationFactory
/// does not set HttpContext.Connection.RemoteIpAddress.
/// </summary>
public sealed class SetupRateLimitApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
    /// <summary>Controls whether the bootstrap state reports setup as complete.</summary>
    public static volatile bool IsSetupComplete = false;

    /// <summary>Configurable requests per window for testing.</summary>
    public static volatile int RequestsPerWindow = 30;

    /// <summary>Configurable window duration in seconds.</summary>
    public static volatile int WindowSeconds = 60;

    /// <summary>Whether to bypass rate limiting for loopback addresses (simulates Development env).</summary>
    public static volatile bool BypassLoopback = false;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GroundUp:Auth:JwtSigningKey"] = "integration-test-signing-key-must-be-at-least-32-bytes-long",
                ["GroundUp:BootstrapAdminToken"] = "test-bootstrap-token-that-is-at-least-32-characters-long"
            });
        });

        base.ConfigureWebHost(builder);
    }

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Replace IBootstrapStateService with a test-controllable implementation
        services.RemoveAll<IBootstrapStateService>();
        services.AddScoped<IBootstrapStateService, RateLimitTestBootstrapStateService>();

        // Register the BootstrapAdminToken auth scheme so the controller doesn't throw
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BootstrapAdminTokenAuthenticationHandler>(
                BootstrapAdminTokenAuthenticationHandler.SchemeName, _ => { });

        // Register rate limiter with a global limiter scoped to /setup paths
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Only rate-limit /setup paths
                if (!context.Request.Path.StartsWithSegments("/setup", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter<string>("non-setup");

                // Check if setup is complete — if so, no limiting
                if (IsSetupComplete)
                    return RateLimitPartition.GetNoLimiter<string>("complete");

                // Determine the remote IP — use test header if present
                // (WebApplicationFactory doesn't set Connection.RemoteIpAddress)
                var ip = context.Connection.RemoteIpAddress;
                if (context.Request.Headers.TryGetValue("X-Test-RemoteIp", out var testIpHeader))
                {
                    if (IPAddress.TryParse(testIpHeader.ToString(), out var parsedIp))
                        ip = parsedIp;
                }

                // Loopback bypass in Development
                if (BypassLoopback && ip is not null && IPAddress.IsLoopback(ip))
                    return RateLimitPartition.GetNoLimiter<string>("loopback-dev");

                var key = ip?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RequestsPerWindow,
                    Window = TimeSpan.FromSeconds(WindowSeconds),
                    QueueLimit = 0,
                });
            });

            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { code = "rate_limited", message = "Too many setup requests from this IP; try again later." }, ct);
            };
        });

        // Register a startup filter that inserts rate limiting middleware early in the pipeline
        services.AddSingleton<IStartupFilter, RateLimitStartupFilter>();
    }
}

/// <summary>
/// Startup filter that inserts rate limiting middleware early in the pipeline
/// for rate limit integration testing. Rate limiting runs before auth per Req 20.5.
/// </summary>
internal sealed class RateLimitStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseRateLimiter();
            next(app);
        };
    }
}

/// <summary>
/// Test implementation of <see cref="IBootstrapStateService"/> for rate limit tests.
/// </summary>
internal sealed class RateLimitTestBootstrapStateService : IBootstrapStateService
{
    public Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SetupRateLimitApiFactory.IsSetupComplete);
    }

    public Task<OperationResult> CompleteSetupAsync(Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        SetupRateLimitApiFactory.IsSetupComplete = true;
        return Task.FromResult(OperationResult.Ok());
    }

    public void InvalidateCache()
    {
        // No-op for tests
    }
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="SetupRateLimitApiFactory"/>
/// across all rate limit integration test classes.
/// </summary>
[CollectionDefinition("SetupRateLimitApi")]
public sealed class SetupRateLimitApiCollection : ICollectionFixture<SetupRateLimitApiFactory>
{
}
