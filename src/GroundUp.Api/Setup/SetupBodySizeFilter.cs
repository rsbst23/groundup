namespace GroundUp.Api.Setup;

using GroundUp.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Endpoint filter that rejects oversized request bodies on setup endpoints.
/// Returns 413 with { code: "payload_too_large", message: "..." }.
/// </summary>
public sealed class SetupBodySizeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var setupOptions = context.HttpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<SetupOptions>>().CurrentValue;
        var contentLength = context.HttpContext.Request.ContentLength;

        if (contentLength is not null && contentLength > setupOptions.MaxRequestBodyBytes)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                code = "payload_too_large",
                message = $"Request body exceeds the {setupOptions.MaxRequestBodyBytes}-byte limit."
            });
            return null;
        }

        return await next(context);
    }
}
