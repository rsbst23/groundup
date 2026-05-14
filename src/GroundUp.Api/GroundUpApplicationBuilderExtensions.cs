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
    /// <item><see cref="JwtAuthenticationMiddleware"/> — validates JWT from cookie or Authorization header</item>
    /// <item><see cref="JwtTenantResolutionMiddleware"/> — resolves tenant from JWT tid claim</item>
    /// <item><see cref="CsrfProtectionMiddleware"/> — validates CSRF token on cookie-auth state-changing requests</item>
    /// <item><see cref="ExceptionHandlingMiddleware"/> — catches unhandled exceptions with correlation ID</item>
    /// </list>
    /// Authentication runs before tenant resolution so that <c>HttpContext.User</c> is
    /// populated before the tenant claim is extracted. CSRF runs after authentication
    /// so it can determine whether the request was cookie-authenticated.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<JwtAuthenticationMiddleware>();
        app.UseMiddleware<JwtTenantResolutionMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        return app;
    }
}
