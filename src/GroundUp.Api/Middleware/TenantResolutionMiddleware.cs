using GroundUp.Core;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Api.Middleware;

/// <summary>
/// Reads the tenant identity from the incoming HTTP request and sets
/// <see cref="TenantContext.TenantId"/>. In Phase 5 this reads from the
/// <c>X-Tenant-Id</c> header as a temporary development/testing mechanism.
/// <para>
/// <strong>Temporary:</strong> Phase 9 will replace this header-based resolution
/// with JWT-based tenant resolution from authentication claims.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware
{
    /// <summary>
    /// The HTTP header name used to carry the tenant identifier.
    /// </summary>
    public const string HeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of <see cref="TenantResolutionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Parses the <c>X-Tenant-Id</c> header and hydrates the scoped
    /// <see cref="TenantContext"/>. If the header is missing or not a valid GUID,
    /// <see cref="TenantContext.TenantId"/> remains <see cref="Guid.Empty"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="tenantContext">The scoped tenant context to hydrate.</param>
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values)
            && Guid.TryParse(values.FirstOrDefault(), out var tenantId))
        {
            tenantContext.TenantId = tenantId;
        }

        await _next(context);
    }
}
