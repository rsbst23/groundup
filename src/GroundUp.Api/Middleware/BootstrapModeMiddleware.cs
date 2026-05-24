using System.Data.Common;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace GroundUp.Api.Middleware;

/// <summary>
/// Redirects non-setup traffic to /setup while bootstrap is incomplete.
/// Registered via UseGroundUpBootstrapMode() before authentication middleware.
/// JSON clients (Accept: application/json parsed via <see cref="MediaTypeHeaderValue"/>)
/// receive 503 instead of 302.
/// </summary>
public sealed class BootstrapModeMiddleware
{
    private static readonly PathString[] AllowedPrefixes =
    [
        new PathString("/setup"),
        new PathString("/_framework"),
        new PathString("/css"),
        new PathString("/js"),
        new PathString("/images"),
        new PathString("/lib")
    ];

    private static readonly PathString[] AllowedExact =
    [
        new PathString("/health"),
        new PathString("/ready")
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<BootstrapModeMiddleware> _logger;
    private static int _hasLoggedSetupMode;

    /// <summary>
    /// Initializes a new instance of <see cref="BootstrapModeMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording bootstrap mode events.</param>
    public BootstrapModeMiddleware(RequestDelegate next, ILogger<BootstrapModeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Checks bootstrap state and either passes through, redirects to /setup,
    /// or returns 503 for JSON clients when setup is incomplete.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var bootstrap = context.RequestServices.GetRequiredService<IBootstrapStateService>();

        bool isComplete;
        try
        {
            isComplete = await bootstrap.IsCompleteAsync(context.RequestAborted);
        }
        catch (DbException dbEx)
        {
            _logger.LogError(dbEx, "Bootstrap state lookup failed; returning 503.");
            await Respond503Async(context, "service_unavailable", "Service temporarily unavailable.");
            return;
        }
        catch (InvalidOperationException missingRow)
        {
            _logger.LogCritical(missingRow, "BootstrapState row is missing.");
            await Respond503Async(context, "bootstrap_state_missing",
                "Bootstrap state row is missing — restore from backup or re-run migration.");
            return;
        }

        if (isComplete)
        {
            await _next(context);
            return;
        }

        if (IsAllowed(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (Interlocked.CompareExchange(ref _hasLoggedSetupMode, 1, 0) == 0)
        {
            _logger.LogInformation(
                "Application is in setup mode; non-setup routes will redirect to /setup");
        }
        else
        {
            _logger.LogDebug("Setup mode redirect for path {Path}", context.Request.Path);
        }

        if (PrefersJson(context.Request))
        {
            await Respond503Async(context, "setup_required",
                "Application setup is not yet complete. Please complete setup at /setup.");
            return;
        }

        context.Response.Redirect("/setup");
    }

    private static bool IsAllowed(PathString path)
    {
        foreach (var prefix in AllowedPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var exact in AllowedExact)
        {
            if (path.Equals(exact))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses the Accept header to determine if the client prefers JSON responses.
    /// Uses <see cref="MediaTypeHeaderValue.ParseList"/> for proper media type parsing.
    /// </summary>
    private static bool PrefersJson(HttpRequest request)
    {
        var values = request.Headers.Accept;
        if (StringValues.IsNullOrEmpty(values))
            return false;

        try
        {
            var parsed = MediaTypeHeaderValue.ParseList(values);
            return parsed.Any(m =>
                m.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task Respond503Async(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { code, message });
    }
}
