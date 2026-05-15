using GroundUp.Auth.Api.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Middleware;

public sealed class CsrfProtectionMiddlewareTests
{
    private readonly IAntiforgery _antiforgery;

    public CsrfProtectionMiddlewareTests()
    {
        _antiforgery = Substitute.For<IAntiforgery>();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task InvokeAsync_SafeMethods_SkipsValidation(string method)
    {
        // Arrange
        var context = CreateContext(method, authSource: "cookie");
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        await _antiforgery.DidNotReceive().ValidateRequestAsync(Arg.Any<HttpContext>());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task InvokeAsync_BearerAuth_SkipsValidation(string method)
    {
        // Arrange
        var context = CreateContext(method, authSource: "header");
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        await _antiforgery.DidNotReceive().ValidateRequestAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_PostMethod_ValidToken_Proceeds()
    {
        // Arrange
        var context = CreateContext("POST", authSource: "cookie");
        _antiforgery.ValidateRequestAsync(context).Returns(Task.CompletedTask);
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_PostMethod_InvalidToken_Returns403()
    {
        // Arrange
        var context = CreateContext("POST", authSource: "cookie");
        _antiforgery.ValidateRequestAsync(context)
            .Returns(Task.FromException(new AntiforgeryValidationException("Invalid token")));
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_PutMethod_EnforcesCsrf()
    {
        // Arrange
        var context = CreateContext("PUT", authSource: "cookie");
        _antiforgery.ValidateRequestAsync(context).Returns(Task.CompletedTask);
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_NoAuthSource_PostMethod_SkipsCsrf()
    {
        // Arrange — no AuthSource in Items (anonymous request)
        var context = CreateContext("POST", authSource: null);
        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        await _antiforgery.DidNotReceive().ValidateRequestAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_AntiforgeryNotRegistered_SkipsGracefully()
    {
        // Arrange — no IAntiforgery in DI
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Method = "POST";
        context.Items["AuthSource"] = "cookie";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — proceeds without error
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task InvokeAsync_CookieAuth_OtherStateChangingMethods_EnforcesCsrf(string method)
    {
        // Arrange
        var context = CreateContext(method, authSource: "cookie");
        _antiforgery.ValidateRequestAsync(context).Returns(Task.CompletedTask);
        var middleware = new CsrfProtectionMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_InvalidToken_ResponseBodyContainsErrorCode()
    {
        // Arrange
        var context = CreateContext("POST", authSource: "cookie");
        context.Response.Body = new MemoryStream();
        _antiforgery.ValidateRequestAsync(context)
            .Returns(Task.FromException(new AntiforgeryValidationException("bad")));
        var middleware = new CsrfProtectionMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert — body contains the documented errorCode and content type is JSON
        Assert.StartsWith("application/json", context.Response.ContentType);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = new StreamReader(context.Response.Body).ReadToEnd();
        Assert.Contains("CSRF_VALIDATION_FAILED", body);
    }

    [Theory]
    [InlineData("Cookie")]
    [InlineData("COOKIE")]
    [InlineData("cOoKiE")]
    public async Task InvokeAsync_AuthSourceCasing_TreatedCaseInsensitively(string casing)
    {
        // Arrange — cookie casing variants should still trigger CSRF enforcement
        var context = CreateContext("POST", authSource: casing);
        _antiforgery.ValidateRequestAsync(context).Returns(Task.CompletedTask);
        var middleware = new CsrfProtectionMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    // --- Helper methods ---

    private DefaultHttpContext CreateContext(string method, string? authSource)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_antiforgery);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Method = method;

        if (authSource is not null)
        {
            context.Items["AuthSource"] = authSource;
        }

        return context;
    }
}
