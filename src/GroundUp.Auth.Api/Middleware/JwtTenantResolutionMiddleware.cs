using GroundUp.Auth.Services.Configuration;
using GroundUp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Resolves the tenant identity from the authenticated JWT <c>tid</c> claim
/// and hydrates the scoped <see cref="TenantContext"/>.
/// Replaces the legacy <c>X-Tenant-Id</c> header-based approach entirely.
/// <para>
/// Must run after <see cref="JwtAuthenticationMiddleware"/> so that
/// <see cref="HttpContext.User"/> is populated before tenant resolution.
/// </para>
/// </summary>
public class JwtTenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of <see cref="JwtTenantResolutionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public JwtTenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Reads the tenant identifier from the authenticated user's claims
    /// and sets <see cref="TenantContext.TenantId"/>. If no authenticated user
    /// or no <c>tid</c> claim is present, <see cref="TenantContext.TenantId"/>
    /// remains <see cref="Guid.Empty"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tidClaim = context.User.FindFirst(options.TenantIdClaimType);
            if (tidClaim is not null && Guid.TryParse(tidClaim.Value, out var tenantId))
            {
                tenantContext.TenantId = tenantId;
            }
        }

        await _next(context);
    }
}
