using FluentAssertions;
using GroundUp.Api.Middleware;
using GroundUp.Core;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Tests.Unit.Api;

/// <summary>
/// Unit tests for <see cref="TenantResolutionMiddleware"/>.
/// Validates header parsing, invalid header handling, missing header behavior,
/// and next delegate invocation.
/// </summary>
public sealed class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ValidGuidHeader_SetsTenantId()
    {
        // Arrange
        var expectedId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TenantResolutionMiddleware.HeaderName] = expectedId.ToString();
        var tenantContext = new TenantContext();

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert
        tenantContext.TenantId.Should().Be(expectedId);
    }

    [Fact]
    public async Task InvokeAsync_InvalidHeader_LeavesGuidEmpty()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(next);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TenantResolutionMiddleware.HeaderName] = "not-a-guid";
        var tenantContext = new TenantContext();

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert
        tenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task InvokeAsync_MissingHeader_LeavesGuidEmpty()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantResolutionMiddleware(next);
        var httpContext = new DefaultHttpContext();
        var tenantContext = new TenantContext();

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert
        tenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TenantResolutionMiddleware(next);
        var httpContext = new DefaultHttpContext();
        var tenantContext = new TenantContext();

        // Act
        await middleware.InvokeAsync(httpContext, tenantContext);

        // Assert
        nextCalled.Should().BeTrue();
    }
}
