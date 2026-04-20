using System.Text.Json;
using GroundUp.Api.Middleware;
using GroundUp.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api;

/// <summary>
/// Unit tests for <see cref="ExceptionHandlingMiddleware"/>.
/// Validates exception-to-HTTP mapping, correlation ID inclusion,
/// content type, and pass-through behavior.
/// </summary>
public sealed class ExceptionHandlingMiddlewareTests
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = Substitute.For<ILogger<ExceptionHandlingMiddleware>>();

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private async Task<JsonElement> InvokeAndReadResponseAsync(
        RequestDelegate next,
        DefaultHttpContext? context = null)
    {
        context ??= CreateHttpContext();
        var middleware = new ExceptionHandlingMiddleware(next, _logger);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task InvokeAsync_NotFoundExceptionThrown_ReturnsHttp404WithNotFoundErrorCode()
    {
        // Arrange
        var context = CreateHttpContext();
        context.Items["CorrelationId"] = "test-correlation";
        RequestDelegate next = _ => throw new NotFoundException("Entity not found");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal("NOT_FOUND", body.GetProperty("errorCode").GetString());
        Assert.Equal("Entity not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_GroundUpExceptionThrown_ReturnsHttp500WithInternalErrorCode()
    {
        // Arrange
        var context = CreateHttpContext();
        RequestDelegate next = _ => throw new GroundUpException("Infrastructure failure");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal("INTERNAL_ERROR", body.GetProperty("errorCode").GetString());
        Assert.Equal("Infrastructure failure", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_GenericExceptionThrown_ReturnsHttp500WithGenericMessage()
    {
        // Arrange
        var context = CreateHttpContext();
        RequestDelegate next = _ => throw new InvalidOperationException("secret internal details");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal("INTERNAL_ERROR", body.GetProperty("errorCode").GetString());
        Assert.Equal("An unexpected error occurred", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_GenericExceptionThrown_DoesNotExposeRawExceptionMessage()
    {
        // Arrange
        var context = CreateHttpContext();
        RequestDelegate next = _ => throw new InvalidOperationException("secret internal details");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();

        Assert.DoesNotContain("secret internal details", json);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionThrown_ResponseContainsCorrelationId()
    {
        // Arrange
        var expectedCorrelationId = "abc-123-correlation";
        var context = CreateHttpContext();
        context.Items["CorrelationId"] = expectedCorrelationId;
        RequestDelegate next = _ => throw new Exception("test");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        var body = JsonDocument.Parse(json).RootElement;

        Assert.Equal(expectedCorrelationId, body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ExceptionThrown_ResponseContentTypeIsApplicationJson()
    {
        // Arrange
        var context = CreateHttpContext();
        RequestDelegate next = _ => throw new Exception("test");

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Contains("application/json", context.Response.ContentType);
    }

    [Fact]
    public async Task InvokeAsync_NoExceptionThrown_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        var context = CreateHttpContext();
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        // Act
        var middleware = new ExceptionHandlingMiddleware(next, _logger);
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}
