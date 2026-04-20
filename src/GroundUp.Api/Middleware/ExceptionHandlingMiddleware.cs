using GroundUp.Core;
using GroundUp.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Api.Middleware;

/// <summary>
/// Middleware that catches unhandled exceptions and maps them to structured
/// JSON error responses with appropriate HTTP status codes.
/// Uses typed exception checks (pattern matching), never string matching.
/// <para>
/// Exception mapping:
/// <list type="bullet">
/// <item><see cref="NotFoundException"/> → HTTP 404, errorCode: NOT_FOUND</item>
/// <item><see cref="GroundUpException"/> → HTTP 500, errorCode: INTERNAL_ERROR</item>
/// <item>Any other Exception → HTTP 500, generic message (hides internals)</item>
/// </list>
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ExceptionHandlingMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for recording exception details.</param>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the next middleware and catches any unhandled exceptions,
    /// mapping them to structured JSON error responses.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message, errorCode) = exception switch
        {
            NotFoundException nfe => (404, nfe.Message, ErrorCodes.NotFound),
            GroundUpException gue => (500, gue.Message, ErrorCodes.InternalError),
            _ => (500, "An unexpected error occurred", ErrorCodes.InternalError)
        };

        var correlationId = context.Items.TryGetValue("CorrelationId", out var id)
            ? id?.ToString()
            : null;

        var response = new
        {
            message,
            errorCode,
            correlationId
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(response);
    }
}
