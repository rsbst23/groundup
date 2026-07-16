using System.Security.Claims;
using GroundUp.Auth.Api.Middleware;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Middleware;

public sealed class TokenRefreshMiddlewareTests
{
    private readonly IAuthSessionService _sessionService;
    private readonly IAuthCookieWriter _cookieWriter;
    private readonly AuthOptions _options;

    public TokenRefreshMiddlewareTests()
    {
        _sessionService = Substitute.For<IAuthSessionService>();
        _cookieWriter = Substitute.For<IAuthCookieWriter>();
        _options = new AuthOptions
        {
            TokenExpirationMinutes = 60,
            AbsoluteSessionLifetimeMinutes = 480,
            TenantIdClaimType = "tid",
            UserIdClaimType = "sub"
        };
    }

    [Fact]
    public async Task InvokeAsync_Unauthenticated_SkipsRefreshAndCallsNext()
    {
        // Arrange
        var (context, nextCalled) = CreateContext(authenticated: false);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
        _cookieWriter.DidNotReceive().WriteAuthCookie(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_NoTidClaim_SkipsRefreshAndCallsNext()
    {
        // Arrange — authenticated but no tid claim (pending-selection Keycloak token)
        var userId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("iat", iat.ToString()),
            new Claim("auth_time", authTime.ToString())
            // No "tid" claim
        };

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_TokenAgeBelow50Percent_SkipsRefreshAndCallsNext()
    {
        // Arrange — token issued 20 minutes ago with 60-minute expiration (< 50% = 30 min)
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_AbsoluteCapReached_SkipsRefreshAndCallsNext()
    {
        // Arrange — token age > 50% but auth_time exceeds absolute session lifetime
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        // auth_time is 500 minutes ago, which exceeds the 480-minute absolute cap
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-500).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_EligibleForRefresh_CallsRefreshAndRewritesCookie()
    {
        // Arrange — token age > 50% and auth_time within absolute cap
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);

        var newToken = "refreshed-jwt-token";
        _sessionService.RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Ok(newToken));

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.Received(1)
            .RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>());
        _cookieWriter.Received(1).WriteAuthCookie(context, newToken);
    }

    [Fact]
    public async Task InvokeAsync_MembershipRevoked_ClearsCookieAndCallsNext()
    {
        // Arrange — refresh returns failure (membership revoked)
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);

        _sessionService.RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Forbidden("Membership revoked"));

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        _cookieWriter.Received(1).ClearAuthCookie(context);
        _cookieWriter.DidNotReceive().WriteAuthCookie(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_RefreshThrowsException_ContinuesPipelineWithoutRefresh()
    {
        // Arrange — exception during refresh should not block
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);

        _sessionService.RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>())
            .Returns<OperationResult<string>>(x => throw new InvalidOperationException("Service unavailable"));

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — pipeline continues despite exception
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task InvokeAsync_InvalidTidClaimValue_SkipsRefreshAndCallsNext()
    {
        // Arrange — tid claim exists but is not a valid Guid
        var userId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tid", "not-a-guid"),
            new Claim("iat", iat.ToString()),
            new Claim("auth_time", authTime.ToString())
        };

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_MissingIatClaim_SkipsRefreshAndCallsNext()
    {
        // Arrange — authenticated with tid but no iat claim
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tid", tenantId.ToString()),
            new Claim("auth_time", DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds().ToString())
            // No "iat" claim
        };

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_MissingAuthTimeClaim_SkipsRefreshAndCallsNext()
    {
        // Arrange — authenticated with tid and iat but no auth_time
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tid", tenantId.ToString()),
            new Claim("iat", iat.ToString())
            // No "auth_time" claim
        };

        var (context, nextCalled) = CreateContext(claims: claims);
        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        await _sessionService.DidNotReceive()
            .RefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task InvokeAsync_TokenAgeExactly50Percent_TriggersRefresh()
    {
        // Arrange — token age exactly at the 50% boundary (30 min for 60-min expiration)
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds();
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);

        var newToken = "refreshed-at-boundary";
        _sessionService.RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Ok(newToken));

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — at exactly 50%, refresh should trigger
        Assert.True(nextCalled.Value);
        await _sessionService.Received(1)
            .RefreshTokenAsync(userId, tenantId, Arg.Any<DateTimeOffset>());
        _cookieWriter.Received(1).WriteAuthCookie(context, newToken);
    }

    [Fact]
    public async Task InvokeAsync_PreservesOriginalAuthTime_InRefreshCall()
    {
        // Arrange — verify the original auth_time is passed to RefreshTokenAsync
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var originalAuthTimeOffset = DateTimeOffset.UtcNow.AddMinutes(-120);
        var iat = DateTimeOffset.UtcNow.AddMinutes(-35).ToUnixTimeSeconds();
        var authTime = originalAuthTimeOffset.ToUnixTimeSeconds();
        var claims = CreateFullClaims(userId, tenantId, iat, authTime);

        var (context, nextCalled) = CreateContext(claims: claims);

        DateTimeOffset capturedAuthTime = default;
        _sessionService.RefreshTokenAsync(userId, tenantId, Arg.Do<DateTimeOffset>(a => capturedAuthTime = a))
            .Returns(OperationResult<string>.Ok("new-token"));

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — the auth_time passed should be the original auth time from the claim
        Assert.True(nextCalled.Value);
        // Unix time conversion loses sub-second precision, so compare at seconds level
        Assert.Equal(originalAuthTimeOffset.ToUnixTimeSeconds(), capturedAuthTime.ToUnixTimeSeconds());
    }

    // --- Helper methods ---

    private TokenRefreshMiddleware CreateMiddleware(RequestDelegate next)
    {
        var logger = Substitute.For<ILogger<TokenRefreshMiddleware>>();
        return new TokenRefreshMiddleware(next, logger);
    }

    private (DefaultHttpContext Context, StrongBox<bool> NextCalled) CreateContext(
        bool authenticated = true,
        Claim[]? claims = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton(_sessionService);
        services.AddSingleton(_cookieWriter);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };

        if (authenticated && claims is not null)
        {
            var identity = new ClaimsIdentity(claims, "GroundUp");
            context.User = new ClaimsPrincipal(identity);
        }
        else if (!authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return (context, new StrongBox<bool>(false));
    }

    private static Claim[] CreateFullClaims(Guid userId, Guid tenantId, long iat, long authTime)
    {
        return
        [
            new Claim("sub", userId.ToString()),
            new Claim("tid", tenantId.ToString()),
            new Claim("iat", iat.ToString()),
            new Claim("auth_time", authTime.ToString())
        ];
    }

    /// <summary>
    /// Strongly-typed box to capture mutable state in closures.
    /// </summary>
    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }
}
