using GroundUp.Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace GroundUp.Api;

/// <summary>
/// Extension methods for registering GroundUp middleware in the ASP.NET Core pipeline.
/// </summary>
public static class GroundUpApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the core GroundUp middleware in the correct order:
    /// <list type="number">
    /// <item><see cref="CorrelationIdMiddleware"/> — generates/reads correlation ID</item>
    /// <item><see cref="ExceptionHandlingMiddleware"/> — catches unhandled exceptions with correlation ID</item>
    /// </list>
    /// <para>
    /// Authentication middleware (JWT validation, tenant resolution from JWT claims, and
    /// CSRF protection) lives in the optional <c>GroundUp.Auth.Api</c> module. Consumers
    /// using authentication should also call <c>UseGroundUpAuth()</c> from
    /// <c>GroundUp.Auth.Api</c> after this method to register the auth pipeline. The
    /// core API module works completely without authentication.
    /// </para>
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        return app;
    }

    /// <summary>
    /// Registers the BootstrapModeMiddleware that gates non-setup traffic during bootstrap.
    /// Should be called AFTER UseRateLimiter() and BEFORE UseAuthentication().
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpBootstrapMode(this IApplicationBuilder app)
    {
        app.UseMiddleware<BootstrapModeMiddleware>();
        return app;
    }
}
