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
    /// <item><see cref="HostTenantResolutionMiddleware"/> — resolves tenant from Host header via subdomain matching, stashes in scoped <c>HostResolvedTenant</c></item>
    /// <item><see cref="JwtAuthenticationMiddleware"/> — validates JWT from cookie or Authorization header and populates <c>HttpContext.User</c></item>
    /// <item><see cref="TokenRefreshMiddleware"/> — sliding-window token refresh when token passes 50% lifetime, bounded by absolute session cap</item>
    /// <item><see cref="JwtTenantResolutionMiddleware"/> — extracts the tenant identifier from the authenticated JWT's <c>tid</c> claim and hydrates the scoped <c>TenantContext</c></item>
    /// <item><see cref="HostTokenReconciliationMiddleware"/> — compares host-resolved tenant against JWT-derived <c>TenantContext</c> and denies cross-tenant data access on mismatch (exempts <c>/auth/*</c> endpoints)</item>
    /// <item><see cref="CsrfProtectionMiddleware"/> — validates anti-forgery tokens on cookie-authenticated state-changing requests</item>
    /// </list>
    /// <para>
    /// Ordering rationale: host resolution is independent of auth state and runs first.
    /// JWT validation must precede refresh (need claims to compute lifetime). Refresh must
    /// precede tenant resolution (may rewrite the cookie with a new token). Tenant resolution
    /// reads the <c>tid</c> from the validated/refreshed claims. Host/token reconciliation runs
    /// after tenant resolution — it needs both the <c>HostResolvedTenant</c> (from host resolution)
    /// and the JWT-derived <c>TenantContext</c> (from the <c>tid</c> claim) to compare them.
    /// CSRF runs last — needs to know if auth source was cookie.
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
        app.UseMiddleware<HostTenantResolutionMiddleware>();
        app.UseMiddleware<JwtAuthenticationMiddleware>();
        app.UseMiddleware<TokenRefreshMiddleware>();
        app.UseMiddleware<JwtTenantResolutionMiddleware>();
        app.UseMiddleware<HostTokenReconciliationMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        return app;
    }
}
