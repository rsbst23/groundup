using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Validates GroundUp-issued JWT tokens from cookies or Authorization headers.
/// Cookie takes precedence over the Authorization header when both are present.
/// Falls back to <see cref="IIdentityProviderService"/> validation if registered.
/// <para>
/// A valid GroundUp token is then checked against <see cref="ITokenRevocationStore"/>: a token issued
/// before the user's access to its tenant ended is not honored, and the request continues anonymous
/// (protected endpoints return 401, and the token cannot be refreshed).
/// </para>
/// <para>
/// If <see cref="AuthOptions.JwtSigningKey"/> is not configured, all validation
/// is skipped and requests proceed with an anonymous identity.
/// </para>
/// <para>
/// Stores the authentication source ("cookie" or "header") in
/// <c>HttpContext.Items["AuthSource"]</c> for downstream CSRF middleware.
/// </para>
/// </summary>
public class JwtAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of <see cref="JwtAuthenticationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public JwtAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Extracts and validates a JWT token from the request cookie or Authorization header,
    /// sets <see cref="HttpContext.User"/> on success, and invokes the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value;

        // If signing key is not configured, skip all validation — auth not set up
        if (string.IsNullOrWhiteSpace(options.JwtSigningKey))
        {
            await _next(context);
            return;
        }

        var (token, authSource) = ExtractToken(context, options);

        if (!string.IsNullOrEmpty(token))
        {
            context.Items["AuthSource"] = authSource;

            var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
            var principal = await tokenService.ValidateTokenAsync(token);

            if (principal is not null)
            {
                // A valid GroundUp token whose access was revoked (the user left the tenant) is treated
                // like an expired one: the request continues anonymous, and the identity-provider
                // fallback is not tried, because the token was ours and is no longer honored.
                if (!await IsRevokedAsync(context, principal, options))
                {
                    context.User = principal;
                }

                await _next(context);
                return;
            }

            // GroundUp validation failed — try IdP fallback if registered
            var idpService = context.RequestServices.GetService<IIdentityProviderService>();
            if (idpService is not null)
            {
                var idpValid = await idpService.ValidateTokenAsync(token);
                if (idpValid)
                {
                    var userInfo = await idpService.GetUserInfoAsync(token);
                    if (userInfo is not null)
                    {
                        context.User = BuildClaimsPrincipal(userInfo, options);
                        await _next(context);
                        return;
                    }
                }
            }
        }

        // No token or all validation failed — proceed with anonymous identity
        await _next(context);
    }

    /// <summary>
    /// Asks <see cref="ITokenRevocationStore"/> whether the token's access was revoked, by user
    /// (<c>sub</c>), tenant (<c>tid</c>, absent for a tenantless token) and issue time (<c>iat</c>).
    /// Fails closed: a token without a parsable <c>sub</c> or <c>iat</c> (<see cref="ITokenService"/>
    /// always writes both), or a store error, counts as revoked. Never logs the token.
    /// </summary>
    private static async Task<bool> IsRevokedAsync(HttpContext context, ClaimsPrincipal principal, AuthOptions options)
    {
        var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger<JwtAuthenticationMiddleware>();

        if (!Guid.TryParse(principal.FindFirst(options.UserIdClaimType)?.Value, out var userId)
            || !long.TryParse(principal.FindFirst("iat")?.Value, out var iatUnix))
        {
            logger?.LogWarning("Rejected a GroundUp token without a valid user id or issue time");
            return true;
        }

        Guid? tenantId = Guid.TryParse(principal.FindFirst(options.TenantIdClaimType)?.Value, out var tid) ? tid : null;

        try
        {
            var store = context.RequestServices.GetRequiredService<ITokenRevocationStore>();
            var revoked = await store.IsRevokedAsync(
                userId, tenantId, DateTimeOffset.FromUnixTimeSeconds(iatUnix), context.RequestAborted);

            if (revoked)
            {
                logger?.LogInformation(
                    "Rejected a revoked token for user {UserId} in tenant {TenantId}", userId, tenantId);
            }

            return revoked;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex,
                "Token revocation check failed for user {UserId}; the request continues unauthenticated", userId);
            return true;
        }
    }

    private static (string? Token, string? Source) ExtractToken(HttpContext context, AuthOptions options)
    {
        // Cookie takes precedence over Authorization header
        if (context.Request.Cookies.TryGetValue(options.CookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken))
        {
            return (cookieToken, "cookie");
        }

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader)
            && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = authHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                return (bearerToken, "header");
            }
        }

        return (null, null);
    }

    private static ClaimsPrincipal BuildClaimsPrincipal(ExternalUserInfo userInfo, AuthOptions options)
    {
        // Reserved claim types that MUST come from the framework, not from external attributes.
        // An external IdP injecting these would let an attacker control authorization context
        // (e.g., set 'tid' to a tenant the user does not belong to, or grant arbitrary roles).
        var reservedClaimTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            // Standard JWT registered claim names
            "sub", "iss", "aud", "exp", "nbf", "iat", "jti",
            // GroundUp-specific authorization claims
            "tid", "role",
            // Configurable claim types — block whichever names the consumer chose
            options.UserIdClaimType,
            options.TenantIdClaimType,
            options.EmailClaimType,
            options.DisplayNameClaimType
        };

        var claims = new List<Claim>
        {
            new(options.UserIdClaimType, userInfo.ExternalUserId),
            new(options.EmailClaimType, userInfo.Email)
        };

        if (!string.IsNullOrWhiteSpace(userInfo.DisplayName))
        {
            claims.Add(new Claim(options.DisplayNameClaimType, userInfo.DisplayName));
        }

        if (userInfo.Attributes is not null)
        {
            foreach (var (key, value) in userInfo.Attributes)
            {
                // Block reserved claim types — protects against IdP-injected authorization claims
                if (reservedClaimTypes.Contains(key))
                {
                    continue;
                }
                claims.Add(new Claim(key, value));
            }
        }

        var identity = new ClaimsIdentity(claims, AuthConstants.ExternalIdpAuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}
