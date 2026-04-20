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
    /// Reads or generates a correlation ID, stores it in HttpContext.Items,
    /// adds it to response headers, and invokes the next middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var values)
            && !string.IsNullOrWhiteSpace(values.FirstOrDefault())
            ? values.First()!
            : Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
