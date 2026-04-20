using GroundUp.Api.Middleware;
using Microsoft.AspNetCore.Builder;

namespace GroundUp.Api;

/// <summary>
/// Extension methods for registering GroundUp middleware in the ASP.NET Core pipeline.
/// </summary>
public static class GroundUpApplicationBuilderExtensions
{
    /// <summary>
    /// Registers GroundUp middleware in the correct order:
    /// <see cref="CorrelationIdMiddleware"/> first (so correlation ID is available),
    /// then <see cref="ExceptionHandlingMiddleware"/> (so exceptions include correlation ID).
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    public static IApplicationBuilder UseGroundUpMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        return app;
    }
}
