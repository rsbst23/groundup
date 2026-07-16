using System.Security.Claims;
using FsCheck;
using FsCheck.Xunit;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="HostTokenReconciliationMiddleware"/>.
/// Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial
/// Validates: Requirements 18.1
/// </summary>
[Trait("Category", "Property")]
public sealed class HostTokenReconciliationPropertyTests
{
    private static readonly AuthOptions DefaultOptions = new()
    {
        UserIdClaimType = "sub",
        TenantIdClaimType = "tid"
    };

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial
    /// For any data path (not starting with /auth/) where HostResolvedTenant is non-null
    /// and doesn't match the JWT tid: access is always denied (regardless of membership
    /// status, tenant type).
    /// **Validates: Requirements 18.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial — data path mismatch always denied")]
    public Property DataPath_HostTokenMismatch_AlwaysDenied(MismatchOnDataPathInput testCase)
    {
        return new Func<bool>(() =>
        {
            // Arrange
            var hostTenant = new TenantDto(
                testCase.HostTenantId,
                "Host Tenant",
                "host-tenant",
                testCase.TenantType,
                OnboardingMode.InviteOnly,
                null,
                testCase.RealmName,
                true);

            var hostResolvedTenant = new HostResolvedTenant { Tenant = hostTenant };
            var tenantContext = new TenantContext { TenantId = testCase.TokenTenantId };

            var userTenantRepository = Substitute.For<IUserTenantRepository>();

            // Configure membership result based on test case
            var memberships = testCase.IsMember
                ? new List<UserTenantDto>
                {
                    new(Guid.NewGuid(), testCase.UserId, testCase.HostTenantId, "ext-1", true)
                }
                : new List<UserTenantDto>();

            userTenantRepository.GetAllMembershipsForUserAsync(testCase.UserId, Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

            var services = new ServiceCollection();
            services.AddSingleton(hostResolvedTenant);
            services.AddSingleton<TenantContext>(tenantContext);
            services.AddSingleton(Options.Create(DefaultOptions));
            services.AddSingleton(userTenantRepository);
            var sp = services.BuildServiceProvider();

            var context = new DefaultHttpContext { RequestServices = sp };
            context.Request.Path = testCase.DataPath;

            // Set authenticated user with tid that mismatches host
            var claims = new[]
            {
                new Claim("sub", testCase.UserId.ToString()),
                new Claim("tid", testCase.TokenTenantId.ToString())
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "GroundUp"));

            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new HostTokenReconciliationMiddleware(
                next, NullLogger<HostTokenReconciliationMiddleware>.Instance);

            // Act
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            // Assert — next should NOT have been called (request denied)
            // and status code should be 4xx
            return !nextCalled && context.Response.StatusCode >= 400;
        }).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial
    /// For any /auth/* path: the middleware never denies even when host tenant and token
    /// tid differ.
    /// **Validates: Requirements 18.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial — auth paths exempt from denial")]
    public Property AuthPath_MismatchedHostAndToken_NeverDenied(MismatchOnAuthPathInput testCase)
    {
        return new Func<bool>(() =>
        {
            // Arrange
            var hostTenant = new TenantDto(
                testCase.HostTenantId,
                "Host Tenant",
                "host-tenant",
                TenantType.Standard,
                OnboardingMode.InviteOnly,
                null,
                null,
                true);

            var hostResolvedTenant = new HostResolvedTenant { Tenant = hostTenant };
            var tenantContext = new TenantContext { TenantId = testCase.TokenTenantId };

            var services = new ServiceCollection();
            services.AddSingleton(hostResolvedTenant);
            services.AddSingleton<TenantContext>(tenantContext);
            services.AddSingleton(Options.Create(DefaultOptions));
            // No need to register userTenantRepository — should not be reached
            var sp = services.BuildServiceProvider();

            var context = new DefaultHttpContext { RequestServices = sp };
            context.Request.Path = testCase.AuthPath;

            // Set authenticated user with tid that mismatches host
            var claims = new[]
            {
                new Claim("sub", testCase.UserId.ToString()),
                new Claim("tid", testCase.TokenTenantId.ToString())
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "GroundUp"));

            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new HostTokenReconciliationMiddleware(
                next, NullLogger<HostTokenReconciliationMiddleware>.Instance);

            // Act
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            // Assert — next SHOULD have been called (request not denied)
            return nextCalled;
        }).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial
    /// For any request where HostResolvedTenant is null OR matches the token tid: the
    /// request always proceeds (next is called).
    /// **Validates: Requirements 18.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 18: Host/Token Mismatch Denial — null or matching host always proceeds")]
    public Property NullOrMatchingHost_AlwaysProceeds(NullOrMatchingHostInput testCase)
    {
        return new Func<bool>(() =>
        {
            // Arrange
            HostResolvedTenant hostResolvedTenant;
            if (testCase.HostTenantIsNull)
            {
                hostResolvedTenant = new HostResolvedTenant { Tenant = null };
            }
            else
            {
                // Matching tenant — same id as the token tid
                var hostTenant = new TenantDto(
                    testCase.TokenTenantId,
                    "Matching Tenant",
                    "matching",
                    TenantType.Standard,
                    OnboardingMode.InviteOnly,
                    null,
                    null,
                    true);
                hostResolvedTenant = new HostResolvedTenant { Tenant = hostTenant };
            }

            var tenantContext = new TenantContext { TenantId = testCase.TokenTenantId };

            var services = new ServiceCollection();
            services.AddSingleton(hostResolvedTenant);
            services.AddSingleton<TenantContext>(tenantContext);
            services.AddSingleton(Options.Create(DefaultOptions));
            var sp = services.BuildServiceProvider();

            var context = new DefaultHttpContext { RequestServices = sp };
            context.Request.Path = testCase.Path;

            // Set authenticated user
            var claims = new[]
            {
                new Claim("sub", testCase.UserId.ToString()),
                new Claim("tid", testCase.TokenTenantId.ToString())
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "GroundUp"));

            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new HostTokenReconciliationMiddleware(
                next, NullLogger<HostTokenReconciliationMiddleware>.Instance);

            // Act
            middleware.InvokeAsync(context).GetAwaiter().GetResult();

            // Assert — next should ALWAYS be called
            return nextCalled;
        }).ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Input for a mismatch scenario on a data path (not /auth/*).
/// </summary>
public sealed record MismatchOnDataPathInput(
    Guid HostTenantId,
    Guid TokenTenantId,
    Guid UserId,
    string DataPath,
    bool IsMember,
    TenantType TenantType,
    string? RealmName);

/// <summary>
/// Input for a mismatch scenario on an /auth/* path.
/// </summary>
public sealed record MismatchOnAuthPathInput(
    Guid HostTenantId,
    Guid TokenTenantId,
    Guid UserId,
    string AuthPath);

/// <summary>
/// Input for a scenario where HostResolvedTenant is null or matches token tid.
/// </summary>
public sealed record NullOrMatchingHostInput(
    Guid TokenTenantId,
    Guid UserId,
    string Path,
    bool HostTenantIsNull);

/// <summary>
/// Custom FsCheck Arbitrary generators for HostTokenReconciliationMiddleware property tests.
/// </summary>
public static class ReconciliationArbitraries
{
    /// <summary>
    /// Generates test inputs for data-path mismatch scenarios.
    /// HostTenantId and TokenTenantId are always different.
    /// DataPath never starts with /auth/ or /tenant-selection.
    /// </summary>
    public static Arbitrary<MismatchOnDataPathInput> MismatchOnDataPathInputArb()
    {
        var dataPathGen = Gen.Elements(
            "/api/projects",
            "/api/users",
            "/api/orders/123",
            "/dashboard",
            "/settings/profile",
            "/api/tenants/members",
            "/health",
            "/api/customers"
        );

        var realmGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>("enterprise-realm"),
            Gen.Constant<string?>("corp-sso")
        );

        var tenantTypeGen = Gen.Elements(TenantType.Standard, TenantType.Enterprise);

        var gen = from hostTenantId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from tokenTenantId in Arb.Generate<Guid>().Where(g => g != Guid.Empty && g != hostTenantId)
                  from userId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from dataPath in dataPathGen
                  from isMember in Arb.Generate<bool>()
                  from tenantType in tenantTypeGen
                  from realmName in realmGen
                  select new MismatchOnDataPathInput(
                      hostTenantId, tokenTenantId, userId, dataPath, isMember, tenantType, realmName);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates test inputs for auth-path exemption scenarios.
    /// HostTenantId and TokenTenantId are always different.
    /// AuthPath always starts with /auth/ or is /auth.
    /// </summary>
    public static Arbitrary<MismatchOnAuthPathInput> MismatchOnAuthPathInputArb()
    {
        var authPathGen = Gen.Elements(
            "/auth/login",
            "/auth/callback",
            "/auth/me",
            "/auth/set-tenant",
            "/auth/refresh",
            "/auth/logout",
            "/auth",
            "/auth/register",
            "/tenant-selection",
            "/tenant-selection/picker"
        );

        var gen = from hostTenantId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from tokenTenantId in Arb.Generate<Guid>().Where(g => g != Guid.Empty && g != hostTenantId)
                  from userId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from authPath in authPathGen
                  select new MismatchOnAuthPathInput(hostTenantId, tokenTenantId, userId, authPath);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates test inputs where the host tenant is null or matches the token tid.
    /// </summary>
    public static Arbitrary<NullOrMatchingHostInput> NullOrMatchingHostInputArb()
    {
        var pathGen = Gen.Elements(
            "/api/projects",
            "/api/users",
            "/dashboard",
            "/auth/me",
            "/auth/login",
            "/settings",
            "/api/orders"
        );

        var gen = from tenantId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from userId in Arb.Generate<Guid>().Where(g => g != Guid.Empty)
                  from path in pathGen
                  from hostIsNull in Arb.Generate<bool>()
                  select new NullOrMatchingHostInput(tenantId, userId, path, hostIsNull);

        return gen.ToArbitrary();
    }
}
