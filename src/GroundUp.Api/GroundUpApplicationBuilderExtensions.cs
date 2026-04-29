using GroundUp.Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace GroundUp.Api;

/// <summary>
/// Extension methods for registering GroundUp middleware in the ASP.NET Core pipeline.
/// </summary>
public static class GroundUpApplicationBuilderExtensions
{
    /// <summary>
    /// Registers GroundUp middleware in the correct order:
    /// <list type="number">
    /// <item><see cref="CorrelationIdMiddleware"/> — generates/reads correlation ID</item>
    /// <item><see cref="TenantResolutionMiddleware"/> — parses X-Tenant-Id header and hydrates TenantContext</item>
    /// <item><see cref="ExceptionHandlingMiddleware"/> — catches unhandled exceptions with correlation ID</item>
    /// </list>
    /// Tenant resolution runs before exception handling so that if a downstream
    /// component throws, the tenant context is already populated for logging.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        return app;
    }
}
