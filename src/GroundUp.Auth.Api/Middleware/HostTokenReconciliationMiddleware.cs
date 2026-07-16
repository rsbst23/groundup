using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Compares the host-resolved tenant (<see cref="HostResolvedTenant"/>) against the
/// JWT-derived tenant (<see cref="TenantContext"/>) and denies cross-tenant data access
/// on mismatch. Runs AFTER <see cref="JwtTenantResolutionMiddleware"/> so that both
/// values are populated.
/// <para>
/// Exempt paths (never denied):
/// <list type="bullet">
///   <item><c>/auth/*</c> — authentication endpoints including <c>set-tenant</c></item>
///   <item>Tenant-selection UI path</item>
/// </list>
/// </para>
/// <para>
/// Decision matrix on mismatch:
/// <list type="bullet">
///   <item>Standard tenant + user is member → 409 TENANT_SWITCH_REQUIRED</item>
///   <item>Enterprise tenant (RealmName set) → 401 REAUTH_REQUIRED</item>
///   <item>User is not a member → 403 Forbidden</item>
/// </list>
/// </para>
/// </summary>
public sealed class HostTokenReconciliationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HostTokenReconciliationMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="HostTokenReconciliationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording reconciliation decisions.</param>
    public HostTokenReconciliationMiddleware(RequestDelegate next, ILogger<HostTokenReconciliationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates host/token tenant reconciliation and short-circuits with a denial response
    /// when the host-resolved tenant conflicts with the JWT tenant context, unless the
    /// request path is exempt.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Exempt paths: /auth/* and tenant-selection UI — never deny these
        if (IsExemptPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var hostResolvedTenant = context.RequestServices.GetRequiredService<HostResolvedTenant>();

        // When HostResolvedTenant is null → proceed normally (no host-based tenant pinning)
        if (hostResolvedTenant.Tenant is null)
        {
            await _next(context);
            return;
        }

        // When unauthenticated (no token / TenantContext.TenantId == Guid.Empty) → proceed normally
        var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
        if (context.User.Identity?.IsAuthenticated != true || tenantContext.TenantId == Guid.Empty)
        {
            await _next(context);
            return;
        }

        // When HostResolvedTenant matches token tid → proceed normally
        if (hostResolvedTenant.Tenant.Id == tenantContext.TenantId)
        {
            await _next(context);
            return;
        }

        // Mismatch detected — determine the appropriate denial response
        _logger.LogInformation(
            "Host/token tenant mismatch. Host tenant: {HostTenantId} ({HostTenantSlug}), Token tenant: {TokenTenantId}",
            hostResolvedTenant.Tenant.Id, hostResolvedTenant.Tenant.Slug, tenantContext.TenantId);

        // Enterprise tenant (RealmName set) → require re-authentication against the tenant's realm
        if (!string.IsNullOrEmpty(hostResolvedTenant.Tenant.RealmName))
        {
            await WriteJsonResponse(context, StatusCodes.Status401Unauthorized, "REAUTH_REQUIRED",
                "Authentication is required for this tenant. Please log in through the tenant's identity provider.");
            return;
        }

        // Standard tenant (RealmName null) — check if user is a member
        var options = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;
        var userIdClaim = context.User.FindFirst(options.UserIdClaimType);

        if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            await WriteJsonResponse(context, StatusCodes.Status403Forbidden, "FORBIDDEN",
                "Access denied. Unable to determine user identity.");
            return;
        }

        var userTenantRepository = context.RequestServices.GetRequiredService<IUserTenantRepository>();
        var membershipsResult = await userTenantRepository.GetAllMembershipsForUserAsync(userId);

        var isMember = membershipsResult.Success
                       && membershipsResult.Data is not null
                       && membershipsResult.Data.Any(m =>
                           m.TenantId == hostResolvedTenant.Tenant.Id && m.IsActive);

        if (isMember)
        {
            // User is a member of the host-resolved standard tenant → tenant switch required
            await WriteJsonResponse(context, StatusCodes.Status409Conflict, "TENANT_SWITCH_REQUIRED",
                "Your current session is scoped to a different tenant. Please switch tenants via POST /auth/set-tenant.");
        }
        else
        {
            // User is NOT a member of the host-resolved tenant → forbidden
            await WriteJsonResponse(context, StatusCodes.Status403Forbidden, "FORBIDDEN",
                "Access denied. You are not a member of this tenant.");
        }
    }

    private static bool IsExemptPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var pathValue = path.Value!;

        // Exempt /auth/* paths (login, callback, me, set-tenant, refresh, logout)
        if (pathValue.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
            || pathValue.Equals("/auth", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Exempt tenant-selection UI path
        if (pathValue.StartsWith("/tenant-selection", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static async Task WriteJsonResponse(HttpContext context, int statusCode, string errorCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message,
            errorCode,
            statusCode
        });
    }
}
