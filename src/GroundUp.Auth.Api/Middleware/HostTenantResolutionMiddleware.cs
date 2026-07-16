using GroundUp.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Resolves the tenant from the HTTP Host header early in the pipeline and
/// stashes the result in the scoped <see cref="HostResolvedTenant"/> service.
/// <para>
/// Must run before <see cref="JwtAuthenticationMiddleware"/> — host resolution
/// is independent of authentication state.
/// </para>
/// </summary>
public class HostTenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HostTenantResolutionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="HostTenantResolutionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording resolution failures.</param>
    public HostTenantResolutionMiddleware(RequestDelegate next, ILogger<HostTenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes <see cref="IHostTenantResolver.ResolveAsync"/> with the current request host
    /// and populates <see cref="HostResolvedTenant.Tenant"/>. On exception, logs a warning,
    /// leaves the tenant as null, and continues the pipeline. Never short-circuits.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var resolver = context.RequestServices.GetRequiredService<IHostTenantResolver>();
        var hostResolvedTenant = context.RequestServices.GetRequiredService<HostResolvedTenant>();

        try
        {
            var tenant = await resolver.ResolveAsync(context.Request.Host, context.RequestAborted);
            hostResolvedTenant.Tenant = tenant;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Host tenant resolution failed for host '{Host}'. Treating as no tenant resolved.", context.Request.Host);
        }

        await _next(context);
    }
}
