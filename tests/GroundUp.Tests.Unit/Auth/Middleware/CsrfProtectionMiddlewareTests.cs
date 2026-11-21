using System.Security.Claims;
using GroundUp.Auth.Api;
using GroundUp.Auth.Api.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData("TRACE")]
    public async Task InvokeAsync_SafeMethods_SkipsValidation(string method)
    {
        // Arrange
        var context = CreateContext(method, authSource: "cookie");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
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
        var middleware = CreateMiddleware(_ =>
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
        var middleware = CreateMiddleware(_ =>
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
        var middleware = CreateMiddleware(_ =>
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

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("PROPFIND")]
    public async Task InvokeAsync_CookieAuth_OtherNonSafeMethods_EnforcesCsrf(string method)
    {
        // Arrange
        var context = CreateContext(method, authSource: "cookie");
        _antiforgery.ValidateRequestAsync(context).Returns(Task.CompletedTask);
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_NoAuthSource_PostMethod_SkipsCsrf()
    {
        // Arrange — no AuthSource in Items (anonymous request)
        var context = CreateContext("POST", authSource: null, authenticated: false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
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
    public async Task InvokeAsync_CookiePresentButNotAuthenticated_SkipsCsrf()
    {
        // Arrange — an expired or invalid cookie: the source is "cookie" but nobody is signed in,
        // so there is no identity to forge (for example POST /auth/login with a stale cookie).
        var context = CreateContext("POST", authSource: "cookie", authenticated: false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
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
    public async Task InvokeAsync_CookieAuth_AntiforgeryNotRegistered_FailsClosedWith500()
    {
        // Arrange — no IAntiforgery in DI
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.Method = "POST";
        context.Items["AuthSource"] = "cookie";
        context.User = AuthenticatedUser();
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — rejected, not let through
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains(AuthConstants.CsrfNotConfiguredErrorCode, ReadBody(context));
    }

    [Fact]
    public async Task InvokeAsync_CookieAuth_InvalidToken_ResponseBodyContainsErrorCode()
    {
        // Arrange
        var context = CreateContext("POST", authSource: "cookie");
        context.Response.Body = new MemoryStream();
        _antiforgery.ValidateRequestAsync(context)
            .Returns(Task.FromException(new AntiforgeryValidationException("bad")));
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert — body contains the documented errorCode and content type is JSON
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Contains(AuthConstants.CsrfValidationFailedErrorCode, ReadBody(context));
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
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        await _antiforgery.Received(1).ValidateRequestAsync(context);
    }

    // --- Helper methods ---

    private static CsrfProtectionMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<CsrfProtectionMiddleware>.Instance);

    private static ClaimsPrincipal AuthenticatedUser() =>
        new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "GroundUp"));

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    private DefaultHttpContext CreateContext(string method, string? authSource, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_antiforgery);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Method = method;

        if (authenticated)
        {
            context.User = AuthenticatedUser();
        }

        if (authSource is not null)
        {
            context.Items["AuthSource"] = authSource;
        }

        return context;
    }
}
