using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Validates anti-forgery tokens on state-changing requests (POST, PUT, PATCH, DELETE and any
/// other method that is not GET, HEAD, OPTIONS or TRACE) when the caller was authenticated by
/// the auth cookie. A browser attaches that cookie to cross-site requests on its own, so those
/// requests must prove they came from the application by sending the request token in the
/// <see cref="AuthConstants.CsrfHeaderName"/> header (obtained from <c>GET /auth/csrf</c>).
/// <para>
/// Skipped when the caller authenticated with an <c>Authorization: Bearer</c> header (a browser
/// never adds that header by itself) or is not authenticated (there is no identity to forge,
/// for example a sign-in started with an expired cookie still present).
/// </para>
/// <para>
/// Fails closed: if <see cref="IAntiforgery"/> is not registered (the host did not call
/// <c>AddGroundUpAuthApi()</c>), cookie-authenticated state-changing requests are rejected with
/// HTTP 500 instead of being let through.
/// </para>
/// </summary>
public class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options,
        HttpMethods.Trace
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CsrfProtectionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for configuration errors.</param>
    public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Validates the anti-forgery token for cookie-authenticated state-changing requests.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresValidation(context))
        {
            await _next(context);
            return;
        }

        var antiforgery = context.RequestServices.GetService<IAntiforgery>();
        if (antiforgery is null)
        {
            _logger.LogError(
                "CSRF protection is not configured: IAntiforgery is not registered. Call services.AddGroundUpAuthApi(). Rejecting {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "CSRF protection is not configured",
                errorCode = AuthConstants.CsrfNotConfiguredErrorCode
            });
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "CSRF token validation failed",
                errorCode = AuthConstants.CsrfValidationFailedErrorCode
            });
            return;
        }

        await _next(context);
    }

    private static bool RequiresValidation(HttpContext context)
    {
        if (SafeMethods.Contains(context.Request.Method))
        {
            return false;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return context.Items.TryGetValue("AuthSource", out var authSource)
            && authSource is string source
            && string.Equals(source, "cookie", StringComparison.OrdinalIgnoreCase);
    }
}
