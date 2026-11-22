using System.Security.Claims;
using GroundUp.Auth.Api.Middleware;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Middleware;

public sealed class JwtAuthenticationMiddlewareTests
{
    private const string ValidSigningKey = "ThisIsAValidSigningKeyThatIs32BytesLong!!";
    private const string CookieName = "AuthToken";

    private readonly ITokenService _tokenService;
    private readonly IIdentityProviderService _idpService;
    private readonly AuthOptions _options;
    private readonly ITokenRevocationStore _store = Substitute.For<ITokenRevocationStore>();

    public JwtAuthenticationMiddlewareTests()
    {
        _tokenService = Substitute.For<ITokenService>();
        _idpService = Substitute.For<IIdentityProviderService>();
        _options = new AuthOptions
        {
            JwtSigningKey = ValidSigningKey,
            CookieName = CookieName,
            Issuer = "TestIssuer",
            Audience = "TestAudience"
        };
    }

    [Fact]
    public async Task InvokeAsync_MissingSigningKey_SkipsValidation()
    {
        // Arrange
        var options = new AuthOptions { JwtSigningKey = "" };
        var (context, nextCalled) = CreateContext(options);
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvokeAsync_CookieToken_ExtractedAndValidated()
    {
        // Arrange
        var principal = CreateAuthenticatedPrincipal();
        _tokenService.ValidateTokenAsync("cookie-token").Returns(principal);

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Cookie = $"{CookieName}=cookie-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("cookie", context.Items["AuthSource"]);
    }

    [Fact]
    public async Task InvokeAsync_BearerToken_ExtractedAndValidated()
    {
        // Arrange
        var principal = CreateAuthenticatedPrincipal();
        _tokenService.ValidateTokenAsync("bearer-token").Returns(principal);

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer bearer-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("header", context.Items["AuthSource"]);
    }

    [Fact]
    public async Task InvokeAsync_CookieTakesPrecedenceOverHeader()
    {
        // Arrange
        var cookiePrincipal = CreateAuthenticatedPrincipal(CookieUserId);
        _tokenService.ValidateTokenAsync("cookie-token").Returns(cookiePrincipal);
        _tokenService.ValidateTokenAsync("bearer-token").Returns(CreateAuthenticatedPrincipal(HeaderUserId));

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Cookie = $"{CookieName}=cookie-token";
        context.Request.Headers.Authorization = "Bearer bearer-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("cookie", context.Items["AuthSource"]);
        Assert.Equal(CookieUserId, context.User.FindFirst("sub")?.Value);
    }

    [Fact]
    public async Task InvokeAsync_NoToken_ProceedsAnonymous()
    {
        // Arrange
        var (context, nextCalled) = CreateContext(_options);
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvokeAsync_InvalidToken_NoIdp_ProceedsAnonymous()
    {
        // Arrange
        _tokenService.ValidateTokenAsync("bad-token").Returns((ClaimsPrincipal?)null);

        var (context, nextCalled) = CreateContextWithoutIdp(_options);
        context.Request.Headers.Authorization = "Bearer bad-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvokeAsync_InvalidToken_IdpRegistered_FallsBackToIdp()
    {
        // Arrange
        _tokenService.ValidateTokenAsync("idp-token").Returns((ClaimsPrincipal?)null);
        _idpService.ValidateTokenAsync("idp-token").Returns(true);
        _idpService.GetUserInfoAsync("idp-token").Returns(
            new ExternalUserInfo("ext-user-123", "idp@test.com", "IdP User", null));

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer idp-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("ext-user-123", context.User.FindFirst("sub")?.Value);
        Assert.Equal("idp@test.com", context.User.FindFirst("email")?.Value);
    }

    [Fact]
    public async Task InvokeAsync_InvalidToken_IdpNotRegistered_SkipsIdpFallback()
    {
        // Arrange
        _tokenService.ValidateTokenAsync("bad-token").Returns((ClaimsPrincipal?)null);

        var (context, nextCalled) = CreateContextWithoutIdp(_options);
        context.Request.Headers.Authorization = "Bearer bad-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvokeAsync_IdpAttributes_ReservedClaimsDenied()
    {
        // Arrange — IdP returns attributes with reserved claim types
        _tokenService.ValidateTokenAsync("idp-token").Returns((ClaimsPrincipal?)null);
        _idpService.ValidateTokenAsync("idp-token").Returns(true);
        _idpService.GetUserInfoAsync("idp-token").Returns(
            new ExternalUserInfo("ext-user-123", "idp@test.com", "IdP User",
                new Dictionary<string, string>
                {
                    { "tid", "injected-tenant-id" },
                    { "role", "injected-admin" },
                    { "sub", "injected-sub" },
                    { "custom_attr", "allowed-value" }
                }));

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer idp-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — reserved claims blocked, custom allowed
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Null(context.User.FindFirst("tid"));
        Assert.Null(context.User.FindFirst("role"));
        // sub should be the one set by the framework, not the injected one
        Assert.Equal("ext-user-123", context.User.FindFirst("sub")?.Value);
        Assert.Equal("allowed-value", context.User.FindFirst("custom_attr")?.Value);
    }

    [Fact]
    public async Task InvokeAsync_EmptyBearerToken_ProceedsAnonymous()
    {
        // Arrange
        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer ";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("bearer abc")]
    [InlineData("BEARER abc")]
    [InlineData("BeArEr abc")]
    public async Task InvokeAsync_BearerSchemeIsCaseInsensitive(string authHeader)
    {
        // Arrange
        var principal = CreateAuthenticatedPrincipal();
        _tokenService.ValidateTokenAsync("abc").Returns(principal);

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = authHeader;
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_NonBearerScheme_Ignored()
    {
        // Arrange — "Token xxx" is not a Bearer scheme
        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Token some-other-scheme-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — no token extracted, anonymous
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        await _tokenService.DidNotReceive().ValidateTokenAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_EmptyCookieValue_FallsThroughToHeader()
    {
        // Arrange — empty cookie value, but a valid header should still be tried
        var principal = CreateAuthenticatedPrincipal();
        _tokenService.ValidateTokenAsync("header-token").Returns(principal);

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Cookie = $"{CookieName}=";
        context.Request.Headers.Authorization = "Bearer header-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — empty cookie skipped, header used
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("header", context.Items["AuthSource"]);
    }

    [Fact]
    public async Task InvokeAsync_IdpValidatesButGetUserInfoReturnsNull_ProceedsAnonymous()
    {
        // Arrange — IdP says token is valid but userinfo lookup returns null
        _tokenService.ValidateTokenAsync("idp-token").Returns((ClaimsPrincipal?)null);
        _idpService.ValidateTokenAsync("idp-token").Returns(true);
        _idpService.GetUserInfoAsync("idp-token").Returns((ExternalUserInfo?)null);

        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer idp-token";
        var middleware = new JwtAuthenticationMiddleware(ctx =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert — anonymous because we can't build a principal without user info
        Assert.True(nextCalled.Value);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task InvokeAsync_IdpUserInfoNullDisplayName_DisplayNameClaimOmitted()
    {
        // Arrange — DisplayName is null/whitespace; the middleware should not add a display-name claim
        _tokenService.ValidateTokenAsync("idp-token").Returns((ClaimsPrincipal?)null);
        _idpService.ValidateTokenAsync("idp-token").Returns(true);
        _idpService.GetUserInfoAsync("idp-token").Returns(
            new ExternalUserInfo("ext-user-456", "idp@test.com", DisplayName: null, Attributes: null));

        var (context, _) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer idp-token";
        var middleware = new JwtAuthenticationMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert — sub and email present, display name absent
        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.NotNull(context.User.FindFirst(_options.UserIdClaimType));
        Assert.NotNull(context.User.FindFirst(_options.EmailClaimType));
        Assert.Null(context.User.FindFirst(_options.DisplayNameClaimType));
    }

    // --- Token revocation (GU-71) ---

    private async Task<(DefaultHttpContext Context, bool NextCalled)> RunWithBearerAsync(ClaimsPrincipal principal)
    {
        _tokenService.ValidateTokenAsync("bearer-token").Returns(principal);
        var (context, nextCalled) = CreateContext(_options);
        context.Request.Headers.Authorization = "Bearer bearer-token";
        var middleware = new JwtAuthenticationMiddleware(_ =>
        {
            nextCalled.Value = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, nextCalled.Value);
    }

    [Fact]
    public async Task InvokeAsync_RevokedToken_ProceedsAnonymousWithoutIdpFallback()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        _store.IsRevokedAsync(userId, tenantId, DateTimeOffset.FromUnixTimeSeconds(iat), Arg.Any<CancellationToken>())
            .Returns(true);

        var (context, nextCalled) = await RunWithBearerAsync(
            CreateAuthenticatedPrincipal(userId.ToString(), tenantId, iat: iat.ToString()));

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        await _idpService.DidNotReceiveWithAnyArgs().ValidateTokenAsync(default!);
    }

    [Fact]
    public async Task InvokeAsync_NotRevoked_AuthenticatesAndPassesUserTenantAndIat()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var (context, _) = await RunWithBearerAsync(
            CreateAuthenticatedPrincipal(userId.ToString(), tenantId, iat: iat.ToString()));

        Assert.True(context.User.Identity?.IsAuthenticated);
        await _store.Received(1).IsRevokedAsync(
            userId, tenantId, DateTimeOffset.FromUnixTimeSeconds(iat), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_TenantlessToken_ChecksWithNullTenant()
    {
        var userId = Guid.NewGuid();

        var (context, _) = await RunWithBearerAsync(CreateAuthenticatedPrincipal(userId.ToString(), withTenant: false));

        Assert.True(context.User.Identity?.IsAuthenticated);
        await _store.Received(1).IsRevokedAsync(userId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoreThrows_FailsClosed()
    {
        _store.IsRevokedAsync(default, default, default, default)
            .ReturnsForAnyArgs<Task<bool>>(_ => throw new InvalidOperationException("store down"));

        var (context, nextCalled) = await RunWithBearerAsync(CreateAuthenticatedPrincipal());

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        await _idpService.DidNotReceiveWithAnyArgs().ValidateTokenAsync(default!);
    }

    [Theory]
    [InlineData("not-a-guid", "1700000000")]
    [InlineData(null, "not-a-number")]
    public async Task InvokeAsync_MissingUserIdOrIat_FailsClosed(string? sub, string iat)
    {
        var principal = CreateAuthenticatedPrincipal(sub, iat: iat);

        var (context, _) = await RunWithBearerAsync(principal);

        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        await _store.DidNotReceiveWithAnyArgs().IsRevokedAsync(default, default, default, default);
    }

    // --- Helper methods ---

    private (DefaultHttpContext Context, StrongBox<bool> NextCalled) CreateContext(AuthOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(_tokenService);
        services.AddSingleton(_idpService);
        services.AddSingleton(_store);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        return (context, new StrongBox<bool>(false));
    }

    private (DefaultHttpContext Context, StrongBox<bool> NextCalled) CreateContextWithoutIdp(AuthOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(_tokenService);
        services.AddSingleton(_store);
        // Intentionally NOT registering IIdentityProviderService
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        return (context, new StrongBox<bool>(false));
    }

    private static readonly string CookieUserId = Guid.NewGuid().ToString();
    private static readonly string HeaderUserId = Guid.NewGuid().ToString();

    /// <summary>A GroundUp principal as <see cref="ITokenService.ValidateTokenAsync"/> returns it.</summary>
    private static ClaimsPrincipal CreateAuthenticatedPrincipal(
        string? sub = null, Guid? tenantId = null, bool withTenant = true, string? iat = null)
    {
        var claims = new List<Claim>
        {
            new("sub", sub ?? Guid.NewGuid().ToString()),
            new("email", "test@test.com"),
            new("iat", iat ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };
        if (withTenant)
        {
            claims.Add(new Claim("tid", (tenantId ?? Guid.NewGuid()).ToString()));
        }
        var identity = new ClaimsIdentity(claims, "GroundUp");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Strongly-typed box to capture mutable state in closures.
    /// </summary>
    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }
}
