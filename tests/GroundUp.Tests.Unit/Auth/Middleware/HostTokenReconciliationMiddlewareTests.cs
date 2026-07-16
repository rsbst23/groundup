using System.Security.Claims;
using GroundUp.Auth.Api.Middleware;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Middleware;

public sealed class HostTokenReconciliationMiddlewareTests
{
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly AuthOptions _options;

    public HostTokenReconciliationMiddlewareTests()
    {
        _userTenantRepository = Substitute.For<IUserTenantRepository>();
        _options = new AuthOptions
        {
            UserIdClaimType = "sub",
            TenantIdClaimType = "tid"
        };
    }

    // --- Match → proceed ---

    [Fact]
    public async Task InvokeAsync_HostTenantMatchesTokenTenant_CallsNextMiddleware()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(tenantId, "acme");
        var userId = Guid.NewGuid();

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.NotEqual(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    // --- No host tenant → proceed ---

    [Fact]
    public async Task InvokeAsync_NoHostResolvedTenant_CallsNextMiddleware()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();

        var (context, nextCalled) = CreateContext(
            hostTenant: null,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    // --- Unauthenticated → proceed ---

    [Fact]
    public async Task InvokeAsync_UnauthenticatedUser_CallsNextMiddleware()
    {
        // Arrange
        var hostTenant = CreateTenantDto(Guid.NewGuid(), "acme");

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: null,
            userId: null,
            authenticated: false,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedButNoTenantContext_CallsNextMiddleware()
    {
        // Arrange — authenticated but TenantContext.TenantId == Guid.Empty (no tid claim)
        var hostTenant = CreateTenantDto(Guid.NewGuid(), "acme");
        var userId = Guid.NewGuid();

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: null,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    // --- Standard mismatch with member → 409 TENANT_SWITCH_REQUIRED ---

    [Fact]
    public async Task InvokeAsync_StandardMismatchUserIsMember_Returns409TenantSwitchRequired()
    {
        // Arrange
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "target-tenant");

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), userId, hostTenantId, "ext-1", IsActive: true),
            new(Guid.NewGuid(), userId, tokenTenantId, "ext-2", IsActive: true)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    // --- Enterprise mismatch → 401 REAUTH_REQUIRED ---

    [Fact]
    public async Task InvokeAsync_EnterpriseMismatch_Returns401ReauthRequired()
    {
        // Arrange — host tenant has RealmName set (enterprise)
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "enterprise-tenant", realmName: "enterprise-realm");

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    // --- Non-member mismatch → 403 Forbidden ---

    [Fact]
    public async Task InvokeAsync_StandardMismatchUserNotMember_Returns403Forbidden()
    {
        // Arrange — user has memberships but NOT in the host-resolved tenant
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "target-tenant");

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), userId, tokenTenantId, "ext-1", IsActive: true),
            new(Guid.NewGuid(), userId, otherTenantId, "ext-2", IsActive: true)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_StandardMismatchInactiveMembership_Returns403Forbidden()
    {
        // Arrange — user has a membership in host tenant but it is inactive
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "target-tenant");

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), userId, hostTenantId, "ext-1", IsActive: false),
            new(Guid.NewGuid(), userId, tokenTenantId, "ext-2", IsActive: true)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: "/api/data");

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    // --- /auth/* path exemption → proceed ---

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/callback")]
    [InlineData("/auth/me")]
    [InlineData("/auth/set-tenant")]
    [InlineData("/auth/refresh")]
    [InlineData("/auth/logout")]
    [InlineData("/auth")]
    public async Task InvokeAsync_AuthPathExemption_CallsNextDespiteMismatch(string path)
    {
        // Arrange — mismatch scenario but path is exempt
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "target-tenant");

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: path);

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    // --- /tenant-selection path exemption → proceed ---

    [Theory]
    [InlineData("/tenant-selection")]
    [InlineData("/tenant-selection/picker")]
    public async Task InvokeAsync_TenantSelectionPathExemption_CallsNextDespiteMismatch(string path)
    {
        // Arrange — mismatch scenario but path is exempt
        var hostTenantId = Guid.NewGuid();
        var tokenTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var hostTenant = CreateTenantDto(hostTenantId, "target-tenant");

        var (context, nextCalled) = CreateContext(
            hostTenant: hostTenant,
            tokenTenantId: tokenTenantId,
            userId: userId,
            authenticated: true,
            path: path);

        var middleware = CreateMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
    }

    // --- Helper methods ---

    private HostTokenReconciliationMiddleware CreateMiddleware(RequestDelegate next)
    {
        var logger = Substitute.For<ILogger<HostTokenReconciliationMiddleware>>();
        return new HostTokenReconciliationMiddleware(next, logger);
    }

    private (DefaultHttpContext Context, StrongBox<bool> NextCalled) CreateContext(
        TenantDto? hostTenant,
        Guid? tokenTenantId,
        Guid? userId,
        bool authenticated,
        string path)
    {
        var hostResolvedTenant = new HostResolvedTenant { Tenant = hostTenant };
        var tenantContext = new TenantContext();
        if (tokenTenantId.HasValue)
        {
            tenantContext.TenantId = tokenTenantId.Value;
        }

        var services = new ServiceCollection();
        services.AddSingleton(hostResolvedTenant);
        services.AddSingleton(tenantContext);
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton(_userTenantRepository);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = sp
        };
        context.Request.Path = path;

        if (authenticated && userId.HasValue)
        {
            var claims = new List<Claim>
            {
                new("sub", userId.Value.ToString())
            };
            if (tokenTenantId.HasValue)
            {
                claims.Add(new Claim("tid", tokenTenantId.Value.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "GroundUp");
            context.User = new ClaimsPrincipal(identity);
        }
        else
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        return (context, new StrongBox<bool>(false));
    }

    private static TenantDto CreateTenantDto(Guid id, string slug, string? realmName = null)
    {
        return new TenantDto(
            Id: id,
            Name: slug,
            Slug: slug,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: realmName,
            IsActive: true);
    }

    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }
}
