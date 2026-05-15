using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Auth.Api.Middleware;

/// <summary>
/// Validates anti-forgery tokens on state-changing requests (POST, PUT, DELETE, PATCH)
/// when authentication is cookie-based. Bearer token authentication is not vulnerable
/// to CSRF and is skipped.
/// <para>
/// Reads the <c>X-CSRF-Token</c> header and validates via ASP.NET Core's
/// <see cref="IAntiforgery"/> service. Returns HTTP 403 Forbidden if validation fails.
/// </para>
/// <para>
/// If <see cref="IAntiforgery"/> is not registered in DI (consuming application
/// did not call <c>AddAntiforgery()</c>), CSRF validation is skipped gracefully.
/// </para>
/// </summary>
public class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Options
    };

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of <see cref="CsrfProtectionMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public CsrfProtectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Validates the anti-forgery token for cookie-authenticated state-changing requests.
    /// Safe methods (GET, HEAD, OPTIONS) and bearer-authenticated requests are skipped.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip safe methods — not vulnerable to CSRF
        if (SafeMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Skip if auth is bearer-based (not vulnerable to CSRF)
        if (context.Items.TryGetValue("AuthSource", out var authSource)
            && authSource is string source
            && string.Equals(source, "header", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Only enforce CSRF on cookie-authenticated state-changing requests
        if (authSource is string cookieSource
            && string.Equals(cookieSource, "cookie", StringComparison.OrdinalIgnoreCase))
        {
            var antiforgery = context.RequestServices.GetService<IAntiforgery>();
            if (antiforgery is not null)
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "CSRF token validation failed",
                        errorCode = "CSRF_VALIDATION_FAILED"
                    });
                    return;
                }
            }
        }

        await _next(context);
    }
}
