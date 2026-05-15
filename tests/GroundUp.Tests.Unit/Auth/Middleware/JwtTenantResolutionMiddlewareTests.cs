using System.Security.Claims;
using GroundUp.Auth.Api.Middleware;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Middleware;

public sealed class JwtTenantResolutionMiddlewareTests
{
    private readonly AuthOptions _options = new()
    {
        TenantIdClaimType = "tid"
    };

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithTidClaim_SetsTenantContext()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var (context, tenantContext) = CreateContext(authenticated: true, tenantId: tenantId);
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(tenantId, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_Unauthenticated_TenantContextRemainsEmpty()
    {
        // Arrange
        var (context, tenantContext) = CreateContext(authenticated: false);
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(Guid.Empty, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutTidClaim_TenantContextRemainsEmpty()
    {
        // Arrange — authenticated but no tid claim
        var (context, tenantContext) = CreateContext(authenticated: true, tenantId: null);
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(Guid.Empty, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_InvalidTidClaimValue_TenantContextRemainsEmpty()
    {
        // Arrange — tid claim is not a valid Guid
        var claims = new[] { new Claim("tid", "not-a-guid") };
        var identity = new ClaimsIdentity(claims, "GroundUp");
        var principal = new ClaimsPrincipal(identity);

        var tenantContext = new TenantContext();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton(tenantContext);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp, User = principal };
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(Guid.Empty, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_CustomTenantIdClaimType_UsesConfiguredClaimType()
    {
        // Arrange
        var customOptions = new AuthOptions { TenantIdClaimType = "tenant_id" };
        var tenantId = Guid.NewGuid();
        var claims = new[] { new Claim("tenant_id", tenantId.ToString()) };
        var identity = new ClaimsIdentity(claims, "GroundUp");
        var principal = new ClaimsPrincipal(identity);

        var tenantContext = new TenantContext();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(customOptions));
        services.AddSingleton(tenantContext);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp, User = principal };
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(tenantId, tenantContext.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        // Arrange
        var (context, _) = CreateContext(authenticated: false);
        var nextCalled = false;
        var middleware = new JwtTenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    // --- Helper methods ---

    private (DefaultHttpContext Context, TenantContext TenantContext) CreateContext(
        bool authenticated, Guid? tenantId = null)
    {
        var tenantContext = new TenantContext();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton(tenantContext);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };

        if (authenticated)
        {
            var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
            if (tenantId.HasValue)
            {
                claims.Add(new Claim("tid", tenantId.Value.ToString()));
            }
            var identity = new ClaimsIdentity(claims, "GroundUp");
            context.User = new ClaimsPrincipal(identity);
        }

        return (context, tenantContext);
    }
}
