using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Sliding-window token refresh middleware. Runs after <see cref="JwtAuthenticationMiddleware"/>
/// and reissues the GroundUp JWT when the token has passed 50% of its lifetime, bounded by
/// the tenant's absolute session lifetime. Both come from the tenant's
/// <see cref="ISessionPolicyProvider"/> policy (the <c>auth.session.*</c> settings), resolved
/// from the token's <c>tid</c> claim.
/// <para>
/// Skips refresh when:
/// <list type="bullet">
///   <item>The request is not authenticated</item>
///   <item>The token has no <c>tid</c> claim (pending-selection / tenant-less)</item>
///   <item>The token age is less than 50% of the tenant's token lifetime</item>
///   <item>The original auth time has exceeded the absolute session cap</item>
/// </list>
/// </para>
/// <para>
/// Never blocks the request — refresh is best-effort. On failure (e.g., membership revoked),
/// the cookie is cleared and the pipeline continues; downstream authorization will reject naturally.
/// </para>
/// </summary>
public class TokenRefreshMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenRefreshMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="TokenRefreshMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording refresh decisions and errors.</param>
    public TokenRefreshMiddleware(RequestDelegate next, ILogger<TokenRefreshMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether the current authenticated token is eligible for refresh and,
    /// if so, reissues the token and rewrites the cookie. Never throws or blocks the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await TryRefreshAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token refresh failed unexpectedly. Continuing pipeline without refresh.");
        }

        await _next(context);
    }

    private async Task TryRefreshAsync(HttpContext context)
    {
        // 1. Skip if not authenticated
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;

        // 2. Skip if no tid claim (pending-selection Keycloak token or tenant-less state)
        var tidClaim = context.User.FindFirst(options.TenantIdClaimType);
        if (tidClaim is null || !Guid.TryParse(tidClaim.Value, out var tenantId))
        {
            return;
        }

        // 3. Read iat claim and compute token age
        var iatClaim = context.User.FindFirst("iat");
        if (iatClaim is null || !long.TryParse(iatClaim.Value, out var iatUnix))
        {
            return;
        }

        // The tenant's effective session policy. Resolved here from the token's tid (not from the
        // request's tenant context) because this middleware runs before tenant resolution.
        var policy = await context.RequestServices
            .GetRequiredService<ISessionPolicyProvider>()
            .GetPolicyForTenantAsync(tenantId, context.RequestAborted);

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix);
        var tokenAge = DateTimeOffset.UtcNow - issuedAt;
        var halfLifetime = TimeSpan.FromTicks(policy.TokenLifetime.Ticks / 2);

        // 4. Skip if token age < 50% of the token lifetime
        if (tokenAge < halfLifetime)
        {
            return;
        }

        // 5. Read auth_time claim and compute elapsed since original authentication
        var authTimeClaim = context.User.FindFirst("auth_time");
        if (authTimeClaim is null || !long.TryParse(authTimeClaim.Value, out var authTimeUnix))
        {
            return;
        }

        var originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        var elapsedSinceAuth = DateTimeOffset.UtcNow - originalAuthTime;

        // 6. Skip if absolute session cap reached (user must re-authenticate)
        if (elapsedSinceAuth >= policy.AbsoluteLifetime)
        {
            return;
        }

        // 7. Extract userId from sub claim
        var userIdClaim = context.User.FindFirst(options.UserIdClaimType);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return;
        }

        // 8. Attempt refresh via IAuthSessionService
        var sessionService = context.RequestServices.GetRequiredService<IAuthSessionService>();
        var cookieWriter = context.RequestServices.GetRequiredService<IAuthCookieWriter>();

        var result = await sessionService.RefreshTokenAsync(userId, tenantId, originalAuthTime);

        if (result.Success && !string.IsNullOrEmpty(result.Data))
        {
            // Success: rewrite the cookie with the new token
            await cookieWriter.WriteAuthCookieAsync(context, result.Data);
        }
        else
        {
            // Failure (membership revoked, or the session reached its lifetime): clear cookie, continue pipeline
            _logger.LogWarning(
                "Token refresh denied for user {UserId} in tenant {TenantId} ({ErrorCode}). Clearing cookie.",
                userId, tenantId, result.ErrorCode);
            await cookieWriter.ClearAuthCookieAsync(context);
        }
    }
}
