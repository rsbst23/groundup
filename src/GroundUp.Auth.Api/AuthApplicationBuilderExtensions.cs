using GroundUp.Auth.Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace GroundUp.Auth.Api;

/// <summary>
/// Extension methods for registering GroundUp authentication middleware
/// in the ASP.NET Core pipeline.
/// </summary>
public static class AuthApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the GroundUp authentication middleware in the correct order:
    /// <list type="number">
    /// <item><see cref="JwtAuthenticationMiddleware"/> — validates JWT from cookie or Authorization header and populates <c>HttpContext.User</c></item>
    /// <item><see cref="JwtTenantResolutionMiddleware"/> — extracts the tenant identifier from the authenticated JWT's <c>tid</c> claim and hydrates the scoped <c>TenantContext</c></item>
    /// <item><see cref="CsrfProtectionMiddleware"/> — validates anti-forgery tokens on cookie-authenticated state-changing requests</item>
    /// </list>
    /// <para>
    /// Ordering rationale: authentication must run before tenant resolution so that
    /// <c>HttpContext.User</c> is populated before the tenant claim is extracted. CSRF
    /// must run after authentication so it can determine whether the request was
    /// cookie-authenticated (the only auth mode vulnerable to CSRF).
    /// </para>
    /// <para>
    /// This method must be called <em>after</em> <c>UseGroundUpMiddleware()</c> from
    /// <c>GroundUp.Api</c> so that correlation ID and exception handling wrap the auth
    /// pipeline. Consumers that do not need authentication can omit this call entirely —
    /// the core GroundUp.Api module functions without it.
    /// </para>
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpAuth(this IApplicationBuilder app)
    {
        app.UseMiddleware<JwtAuthenticationMiddleware>();
        app.UseMiddleware<JwtTenantResolutionMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        return app;
    }
}
