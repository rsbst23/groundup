using System.Text;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="SimpleSignupFlowHandler"/>.
/// Feature: phase-10c-api-alignment, Properties for SimpleSignup flow.
/// **Validates: Requirements 3.2, 3.4**
/// </summary>
[Trait("Category", "Property")]
public sealed class SimpleSignupFlowHandlerPropertyTests
{
    private static readonly Guid DefaultTenantId = Guid.NewGuid();

    /// <summary>
    /// Property: For any valid callback context with a default tenant configured,
    /// the handler always produces a FlowResult (either success or known error — never throws).
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SimpleSignupArbitraries) })]
    public Property HandleCallbackAsync_WithDefaultTenantConfigured_NeverThrows(ValidSimpleSignupInput input)
    {
        Func<bool> property = () =>
        {
            var (sut, _) = CreateHandler(
                defaultTenantSlug: input.TenantSlug,
                tenantExists: true,
                tenantIsActive: true,
                userExists: input.UserExists,
                alreadyMember: input.AlreadyMember,
                codeExchangeSucceeds: input.CodeExchangeSucceeds,
                userinfoSucceeds: input.UserinfoSucceeds);

            var context = CreateCallbackContext(input.Nonce);

            try
            {
                var result = sut.HandleCallbackAsync(context).GetAwaiter().GetResult();
                // Must always produce a FlowResult (never null)
                return result is not null;
            }
            catch
            {
                return false;
            }
        };

        return property.ToProperty();
    }

    /// <summary>
    /// Property: When default-tenant-slug is configured and tenant exists and all preceding
    /// steps succeed (code exchange, nonce, userinfo), result is always Success with a non-null token.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SimpleSignupArbitraries) })]
    public Property HandleCallbackAsync_AllStepsSucceed_AlwaysReturnsSuccessWithToken(SuccessPathInput input)
    {
        Func<bool> property = () =>
        {
            var (sut, _) = CreateHandler(
                defaultTenantSlug: input.TenantSlug,
                tenantExists: true,
                tenantIsActive: true,
                userExists: input.UserExists,
                alreadyMember: input.AlreadyMember,
                codeExchangeSucceeds: true,
                userinfoSucceeds: true);

            var context = CreateCallbackContext(input.Nonce);

            var result = sut.HandleCallbackAsync(context).GetAwaiter().GetResult();

            return result.IsSuccess && result.Token is not null;
        };

        return property.ToProperty();
    }

    #region Factory Helpers

    private static (SimpleSignupFlowHandler Handler, DefaultHttpContext HttpContext) CreateHandler(
        string? defaultTenantSlug,
        bool tenantExists,
        bool tenantIsActive,
        bool userExists,
        bool alreadyMember,
        bool codeExchangeSucceeds,
        bool userinfoSucceeds)
    {
        var identityProviderService = Substitute.For<IIdentityProviderService>();
        var userRepository = Substitute.For<IUserRepository>();
        var userTenantRepository = Substitute.For<IUserTenantRepository>();
        var tenantRepository = Substitute.For<ITenantRepository>();
        var tokenService = Substitute.For<ITokenService>();
        var authCookieWriter = Substitute.For<IAuthCookieWriter>();
        var authFlowStateService = Substitute.For<IAuthFlowStateService>();
        var settingsService = Substitute.For<ISettingsService>();
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var logger = Substitute.For<ILogger<SimpleSignupFlowHandler>>();

        var httpContext = new DefaultHttpContext();
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Code exchange
        if (codeExchangeSucceeds)
        {
            var idToken = CreateIdTokenWithNonce("test-nonce");
            identityProviderService.ExchangeCodeForTokensAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                    Arg.Any<string?>(), Arg.Any<string?>())
                .Returns(new TokenResponseDto("access-token", "refresh-token", 3600, idToken));
        }
        else
        {
            identityProviderService.ExchangeCodeForTokensAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                    Arg.Any<string?>(), Arg.Any<string?>())
                .Returns((TokenResponseDto?)null);
        }

        // Userinfo
        if (userinfoSucceeds)
        {
            identityProviderService.GetUserInfoAsync(Arg.Any<string>())
                .Returns(new ExternalUserInfo("ext-user-id", "user@test.com", "Test User", null));
        }
        else
        {
            identityProviderService.GetUserInfoAsync(Arg.Any<string>())
                .Returns((ExternalUserInfo?)null);
        }

        // User resolution
        var userId = Guid.NewGuid();
        var userDto = new UserDto(userId, "ext-user-id", "user@test.com", "Test User", true);

        if (userExists)
        {
            userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<UserDto>.Ok(userDto));
        }
        else
        {
            userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<UserDto>.Fail("Not found", 404));
            userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<UserDto>.Ok(userDto));
        }

        // Default tenant slug setting
        if (!string.IsNullOrWhiteSpace(defaultTenantSlug))
        {
            settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(defaultTenantSlug));
        }
        else
        {
            settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Fail("Not found", 404));
        }

        // Default role — not configured (simplifies property test)
        settingsService.GetAsync<string>("auth.application.default-role", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Not found", 404));

        // Tenant lookup
        if (tenantExists)
        {
            var tenantDto = new TenantDto(
                DefaultTenantId, "Community", defaultTenantSlug ?? "community",
                TenantType.Standard, OnboardingMode.InviteOnly, null, null, tenantIsActive);

            tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<TenantDto>.Ok(tenantDto));
        }
        else
        {
            tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<TenantDto>.Fail("Not found", 404));
        }

        // Membership check
        if (alreadyMember)
        {
            var memberships = new List<UserTenantDto>
            {
                new(Guid.NewGuid(), userId, DefaultTenantId, "ext-user-id", IsActive: true)
            };
            userTenantRepository.GetAllMembershipsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
        }
        else
        {
            userTenantRepository.GetAllMembershipsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>()));
        }

        // Membership creation
        userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0)));

        // Token generation
        tokenService.GenerateTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<System.Security.Claims.Claim>?>())
            .Returns("generated-token-xyz");

        var handler = new SimpleSignupFlowHandler(
            identityProviderService,
            userRepository,
            userTenantRepository,
            tenantRepository,
            tokenService,
            authCookieWriter,
            authFlowStateService,
            settingsService,
            httpContextAccessor,
            logger);

        return (handler, httpContext);
    }

    private static FlowCallbackContext CreateCallbackContext(string nonce)
    {
        var consumedState = new AuthFlowStateDto(
            Id: Guid.NewGuid(),
            FlowType: FlowType.SimpleSignup,
            Status: FlowStatus.Consumed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: "state-token",
            CodeVerifier: "code-verifier-0123456789012345678901234567890123456789",
            RedirectUri: "https://app.example.com/auth/callback",
            OrganizationName: null,
            Nonce: nonce,
            CreatedByIp: "192.168.1.1",
            CreatedByUserAgent: "TestBrowser/1.0",
            ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            ConsumedAt: DateTime.UtcNow,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);

        return new FlowCallbackContext(
            AuthorizationCode: "auth-code-123",
            CodeVerifier: "code-verifier-0123456789012345678901234567890123456789",
            RedirectUri: "https://app.example.com/auth/callback",
            ConsumedState: consumedState,
            HostResolvedTenant: null);
    }

    private static string CreateIdTokenWithNonce(string nonce)
    {
        var header = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{{\"nonce\":\"{nonce}\",\"sub\":\"test\"}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{header}.{payload}.fake-signature";
    }

    #endregion
}

// --- Test data types ---

/// <summary>
/// Input for the never-throws property test. Varies code exchange, userinfo success,
/// user existence, and membership state.
/// </summary>
public sealed record ValidSimpleSignupInput(
    string TenantSlug,
    string Nonce,
    bool UserExists,
    bool AlreadyMember,
    bool CodeExchangeSucceeds,
    bool UserinfoSucceeds)
{
    public override string ToString() =>
        $"Slug={TenantSlug}, UserExists={UserExists}, AlreadyMember={AlreadyMember}, " +
        $"CodeExchange={CodeExchangeSucceeds}, Userinfo={UserinfoSucceeds}";
}

/// <summary>
/// Input for the success-path property test where all preceding steps succeed.
/// </summary>
public sealed record SuccessPathInput(
    string TenantSlug,
    string Nonce,
    bool UserExists,
    bool AlreadyMember)
{
    public override string ToString() =>
        $"Slug={TenantSlug}, UserExists={UserExists}, AlreadyMember={AlreadyMember}";
}

/// <summary>
/// Custom FsCheck Arbitrary generators for SimpleSignup property tests.
/// </summary>
public static class SimpleSignupArbitraries
{
    private static readonly string[] ValidSlugs =
    {
        "community", "default-org", "workspace", "shared-team",
        "public-space", "main-org", "team-alpha", "dev-hub"
    };

    /// <summary>
    /// Generates valid inputs for the never-throws property.
    /// The nonce is always "test-nonce" to match the id_token generated in the mock.
    /// </summary>
    public static Arbitrary<ValidSimpleSignupInput> ValidSimpleSignupInputArb()
    {
        var gen = from slug in Gen.Elements(ValidSlugs)
                  from userExists in Arb.Generate<bool>()
                  from alreadyMember in Arb.Generate<bool>()
                  from codeExchangeSucceeds in Arb.Generate<bool>()
                  from userinfoSucceeds in Arb.Generate<bool>()
                  select new ValidSimpleSignupInput(slug, "test-nonce", userExists, alreadyMember, codeExchangeSucceeds, userinfoSucceeds);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates inputs for the success-path property where code exchange and userinfo succeed.
    /// </summary>
    public static Arbitrary<SuccessPathInput> SuccessPathInputArb()
    {
        var gen = from slug in Gen.Elements(ValidSlugs)
                  from userExists in Arb.Generate<bool>()
                  from alreadyMember in Arb.Generate<bool>()
                  select new SuccessPathInput(slug, "test-nonce", userExists, alreadyMember);

        return gen.ToArbitrary();
    }
}
