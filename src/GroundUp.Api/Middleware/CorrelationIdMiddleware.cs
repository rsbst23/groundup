using Microsoft.AspNetCore.Http;

namespace GroundUp.Api.Middleware;

/// <summary>
/// Middleware that reads or generates a correlation ID per request,
/// stores it in <see cref="HttpContext.Items"/>, and adds it to response headers.
/// Must be registered before <see cref="ExceptionHandlingMiddleware"/> so the
/// correlation ID is available when exceptions are caught.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>
    /// The HTTP header name used for correlation ID propagation.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// The maximum accepted length of an incoming correlation ID.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Returns true when <paramref name="value"/> may be used as a correlation ID: 1 to
    /// <see cref="MaxLength"/> characters, each one of <c>A-Z a-z 0-9 . _ : -</c>. This covers GUIDs,
    /// W3C trace and span ids, and common request-id formats. The value is written to logs, error
    /// responses and response headers, so anything else (control characters, spaces, separators,
    /// non-ASCII) is rejected to prevent log injection and header manipulation (NIST 800-53 SI-10, AU-9).
    /// </summary>
    /// <param name="value">The candidate correlation ID.</param>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!IsAllowedChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or ':' or '-';

    /// <summary>
    /// Reads the incoming correlation ID when it is valid (see <see cref="IsValid"/>), otherwise generates
    /// a new GUID. Stores it in HttpContext.Items, adds it to response headers, and invokes the next
    /// middleware. A rejected incoming value is discarded and never logged or echoed.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers.TryGetValue(HeaderName, out var values)
            ? values.FirstOrDefault()
            : null;
        var correlationId = IsValid(incoming) ? incoming! : Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
