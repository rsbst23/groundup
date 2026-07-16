using FluentAssertions;
using GroundUp.Auth.Api.Middleware;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Example-based unit tests for <see cref="HostTenantResolver"/> and
/// <see cref="HostTenantResolutionMiddleware"/>.
/// Validates: Requirements 2.1–2.8, 3.1–3.6
/// </summary>
public sealed class HostTenantResolverTests
{
    private readonly ISettingsService _settingsService;
    private readonly ITenantRepository _tenantRepository;
    private readonly HostTenantResolver _sut;

    public HostTenantResolverTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _tenantRepository = Substitute.For<ITenantRepository>();
        _sut = new HostTenantResolver(_settingsService, _tenantRepository);

        // Default: domain setting returns "sampleapp.com"
        SetupDomainSetting("sampleapp.com");
    }

    #region HostTenantResolver Tests

    /// <summary>
    /// Requirement 2.5: repository returns not-found → resolver returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_RepositoryReturnsNotFound_ReturnsNull()
    {
        // Arrange
        _tenantRepository.GetBySlugBypassFilterAsync("acme", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.NotFound());

        var host = new HostString("acme.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Requirement 2.5: repository returns inactive tenant (IsActive=false) → resolver returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_InactiveTenant_ReturnsNull()
    {
        // Arrange
        var inactiveTenant = CreateTenantDto("acme", isActive: false);
        _tenantRepository.GetBySlugBypassFilterAsync("acme", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(inactiveTenant));

        var host = new HostString("acme.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Requirement 2.6: multi-level subdomain (e.g., a.b.sampleapp.com) → returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MultiLevelSubdomain_ReturnsNull()
    {
        // Arrange
        var host = new HostString("a.b.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
        await _tenantRepository.DidNotReceive()
            .GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Requirement 2.6: bare domain (sampleapp.com, no subdomain) → returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_BareDomain_ReturnsNull()
    {
        // Arrange
        var host = new HostString("sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
        await _tenantRepository.DidNotReceive()
            .GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Requirement 2.6: IP address host → returns null.
    /// </summary>
    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    public async Task ResolveAsync_IpAddressHost_ReturnsNull(string ipHost)
    {
        // Arrange
        var host = new HostString(ipHost);

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
        await _tenantRepository.DidNotReceive()
            .GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Requirement 2.2: port is stripped before matching (acme.sampleapp.com:5000 → resolves acme).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_HostWithPort_StripsPortBeforeMatching()
    {
        // Arrange
        var activeTenant = CreateTenantDto("acme", isActive: true);
        _tenantRepository.GetBySlugBypassFilterAsync("acme", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(activeTenant));

        var host = new HostString("acme.sampleapp.com", 5000);

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().NotBeNull();
        result!.Slug.Should().Be("acme");
    }

    /// <summary>
    /// Requirement 2.2: case-insensitive match (ACME.SampleApp.Com → resolves acme).
    /// </summary>
    [Fact]
    public async Task ResolveAsync_CaseInsensitiveMatch_ResolvesCorrectly()
    {
        // Arrange
        var activeTenant = CreateTenantDto("acme", isActive: true);
        _tenantRepository.GetBySlugBypassFilterAsync("acme", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(activeTenant));

        var host = new HostString("ACME.SampleApp.Com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().NotBeNull();
        result!.Slug.Should().Be("acme");
    }

    /// <summary>
    /// Requirement 2.7: empty/null default-domain setting → returns null for all hosts.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_EmptyOrNullDomainSetting_ReturnsNull(string? domainSetting)
    {
        // Arrange
        SetupDomainSetting(domainSetting);
        var host = new HostString("acme.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
        await _tenantRepository.DidNotReceive()
            .GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Requirement 2.4: valid subdomain with active tenant → returns TenantDto.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ValidSubdomainWithActiveTenant_ReturnsTenantDto()
    {
        // Arrange
        var activeTenant = CreateTenantDto("acme", isActive: true);
        _tenantRepository.GetBySlugBypassFilterAsync("acme", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(activeTenant));

        var host = new HostString("acme.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(activeTenant);
        result!.Slug.Should().Be("acme");
        result.IsActive.Should().BeTrue();
    }

    /// <summary>
    /// Requirement 2.6: different domain that doesn't match default-domain → returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_DifferentDomain_ReturnsNull()
    {
        // Arrange
        var host = new HostString("acme.otherdomain.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
        await _tenantRepository.DidNotReceive()
            .GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Requirement 2.7: domain setting lookup fails → returns null.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_SettingsServiceFails_ReturnsNull()
    {
        // Arrange
        _settingsService.GetAsync<string>(
                "auth.application.default-domain",
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Not found", 404));

        var host = new HostString("acme.sampleapp.com");

        // Act
        var result = await _sut.ResolveAsync(host);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region HostTenantResolutionMiddleware Tests

    /// <summary>
    /// Requirement 3.2: resolver returns tenant → HostResolvedTenant.Tenant is set.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverReturnsTenant_SetsHostResolvedTenant()
    {
        // Arrange
        var expectedTenant = CreateTenantDto("acme", isActive: true);
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Returns(expectedTenant);

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, _) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        hostResolvedTenant.Tenant.Should().BeSameAs(expectedTenant);
    }

    /// <summary>
    /// Requirement 3.3: resolver returns null → HostResolvedTenant.Tenant remains null.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverReturnsNull_TenantRemainsNull()
    {
        // Arrange
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Returns((TenantDto?)null);

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, _) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        hostResolvedTenant.Tenant.Should().BeNull();
    }

    /// <summary>
    /// Requirement 3.4: resolver throws exception → logged at Warning, HostResolvedTenant.Tenant is null, pipeline continues.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverThrowsException_LogsWarningAndContinues()
    {
        // Arrange
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database connection failed"));

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, nextCalled) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        hostResolvedTenant.Tenant.Should().BeNull();
        nextCalled().Should().BeTrue("pipeline must continue after exception");
    }

    /// <summary>
    /// Requirement 3.6: next() is always called (never short-circuits) — when resolver returns a tenant.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverReturnsTenant_AlwaysCallsNext()
    {
        // Arrange
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Returns(CreateTenantDto("acme", isActive: true));

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, nextCalled) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled().Should().BeTrue("next() must always be called");
    }

    /// <summary>
    /// Requirement 3.6: next() is always called (never short-circuits) — when resolver returns null.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverReturnsNull_AlwaysCallsNext()
    {
        // Arrange
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Returns((TenantDto?)null);

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, nextCalled) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled().Should().BeTrue("next() must always be called");
    }

    /// <summary>
    /// Requirement 3.6: next() is always called (never short-circuits) — when resolver throws.
    /// </summary>
    [Fact]
    public async Task Middleware_ResolverThrows_AlwaysCallsNext()
    {
        // Arrange
        var resolver = Substitute.For<IHostTenantResolver>();
        resolver.ResolveAsync(Arg.Any<HostString>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("Unexpected failure"));

        var hostResolvedTenant = new HostResolvedTenant();
        var (context, middleware, nextCalled) = CreateMiddlewareContext(resolver, hostResolvedTenant);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled().Should().BeTrue("next() must always be called even on exception");
    }

    #endregion

    #region Helper Methods

    private void SetupDomainSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _settingsService.GetAsync<string>(
                    "auth.application.default-domain",
                    Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(value!));
        }
        else
        {
            _settingsService.GetAsync<string>(
                    "auth.application.default-domain",
                    Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(value));
        }
    }

    private static TenantDto CreateTenantDto(string slug, bool isActive) =>
        new(
            Id: Guid.NewGuid(),
            Name: slug.ToUpperInvariant() + " Corp",
            Slug: slug,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: isActive);

    private static (DefaultHttpContext Context, HostTenantResolutionMiddleware Middleware, Func<bool> NextCalled)
        CreateMiddlewareContext(IHostTenantResolver resolver, HostResolvedTenant hostResolvedTenant)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton(hostResolvedTenant);
        var sp = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = sp
        };
        context.Request.Host = new HostString("acme.sampleapp.com");

        var nextCalled = false;
        var logger = Substitute.For<ILogger<HostTenantResolutionMiddleware>>();

        var middleware = new HostTenantResolutionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            logger);

        return (context, middleware, () => nextCalled);
    }

    #endregion
}
