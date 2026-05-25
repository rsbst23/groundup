using System.Data.Common;
using System.Text.Json;
using GroundUp.Api.Middleware;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Api.Middleware;

/// <summary>
/// Unit tests for <see cref="BootstrapModeMiddleware"/>.
/// Validates path-based routing, redirect behavior, JSON 503 responses,
/// and error handling when the bootstrap state service is unavailable.
/// Requirements: 7.1–7.8
/// </summary>
public sealed class BootstrapModeMiddlewareTests
{
    private readonly IBootstrapStateService _bootstrapService = Substitute.For<IBootstrapStateService>();
    private readonly ILogger<BootstrapModeMiddleware> _logger = Substitute.For<ILogger<BootstrapModeMiddleware>>();

    private DefaultHttpContext CreateHttpContext(string path, string? acceptHeader = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_bootstrapService);
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (acceptHeader is not null)
        {
            context.Request.Headers.Accept = acceptHeader;
        }

        return context;
    }

    private BootstrapModeMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new BootstrapModeMiddleware(next, _logger);
    }

    #region Setup Complete — Pass Through

    [Fact]
    public async Task InvokeAsync_SetupComplete_PassesThrough()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(true);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/users");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    #endregion

    #region Setup Incomplete — Allowed Paths Pass Through

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_SetupPath_PassesThrough()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/setup/status");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_HealthPath_PassesThrough()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/health");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_ReadyPath_PassesThrough()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/ready");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_FrameworkPath_PassesThrough()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/_framework/blazor.js");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    #endregion

    #region Setup Incomplete — Non-Allowed Paths

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_NonAllowedPath_JsonClient_Returns503()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/users", acceptHeader: "application/json");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(503, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal("setup_required", body.GetProperty("code").GetString());
        Assert.Contains("/setup", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_NonAllowedPath_BrowserClient_Redirects302()
    {
        // Arrange
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/dashboard");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
        Assert.Equal("/setup", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_NonAllowedPath_TextHtmlAccept_Redirects302()
    {
        // Arrange — text/html Accept header should get redirect, not 503
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/dashboard", acceptHeader: "text/html");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
    }

    #endregion

    #region Segment-Aware Path Matching

    [Fact]
    public async Task InvokeAsync_SetupIncomplete_SetupEvilPath_DoesNotPassThrough()
    {
        // Arrange — "/setup-evil" should NOT match "/setup" (segment-aware matching)
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/setup-evil");

        // Act
        await middleware.InvokeAsync(context);

        // Assert — should redirect, not pass through
        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task InvokeAsync_DbException_Returns503ServiceUnavailable()
    {
        // Arrange — simulate database connectivity failure
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TestDbException("Connection refused"));
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/users");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(503, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal("service_unavailable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_InvalidOperationException_Returns503WithBootstrapStateMissing()
    {
        // Arrange — simulate missing bootstrap state row
        _bootstrapService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bootstrap state row is missing."));
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("/api/users");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(503, context.Response.StatusCode);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal("bootstrap_state_missing", body.GetProperty("code").GetString());
    }

    #endregion

    #region Helpers

    private static async Task<JsonElement> ReadResponseBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Concrete DbException subclass for testing since DbException is abstract.
    /// </summary>
    private sealed class TestDbException : DbException
    {
        public TestDbException(string message) : base(message) { }
    }

    #endregion
}
