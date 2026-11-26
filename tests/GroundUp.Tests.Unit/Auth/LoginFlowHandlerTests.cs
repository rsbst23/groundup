using System.Security.Claims;
using FluentAssertions;
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

public sealed class LoginFlowHandlerTests
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IUserRepository _userRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly ISettingsService _settingsService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LoginFlowHandler> _logger;
    private readonly GroundUp.Core.TenantContext _tenantContext = new();
    private LoginFlowHandler _sut;

    // Reusable test data
    private const string TestAuthCode = "auth-code-login-123";
    private const string TestCodeVerifier = "code-verifier-0123456789012345678901234567890123456789";
    private const string TestRedirectUri = "https://app.example.com/auth/callback";
    private const string TestNonce = "expected-nonce-value";
    private const string TestAccessToken = "keycloak-access-token-abc";
    private const string TestTenantlessToken = "groundup-tenantless-token";
    private const string TestExternalUserId = "keycloak-sub-99999";
    private const string TestEmail = "user@example.com";
    private const string TestDisplayName = "Test User";
    private const string TestGeneratedToken = "groundup-jwt-token-xyz";

    private static readonly Guid TestFlowStateId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId1 = Guid.NewGuid();
    private static readonly Guid TestTenantId2 = Guid.NewGuid();
    private static readonly Guid TestTenantId3 = Guid.NewGuid();

    public LoginFlowHandlerTests()
    {
        _identityProviderService = Substitute.For<IIdentityProviderService>();
        _userRepository = Substitute.For<IUserRepository>();
        _userTenantRepository = Substitute.For<IUserTenantRepository>();
        _tenantRepository = Substitute.For<ITenantRepository>();
        _tokenService = Substitute.For<ITokenService>();
        _authCookieWriter = Substitute.For<IAuthCookieWriter>();
        _authFlowStateService = Substitute.For<IAuthFlowStateService>();
        _settingsService = Substitute.For<ISettingsService>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _logger = Substitute.For<ILogger<LoginFlowHandler>>();

        var httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(httpContext);

        // Tenantless token issuance succeeds unless a test says otherwise
        _tokenService.GenerateTenantlessTokenAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns(TestTenantlessToken);

        _sut = CreateSut();
    }

    private LoginFlowHandler CreateSut(int absoluteLifetimeMinutes = 480) =>
        new(
            _identityProviderService,
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.DecodingIdTokenValidator(),
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.SessionGuard(absoluteLifetimeMinutes),
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.UserResolver(_userRepository),
            _userTenantRepository,
            _tenantRepository,
            _tokenService,
            _authCookieWriter,
            _authFlowStateService,
            _settingsService,
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.DefaultRoleAssigner(_settingsService),
            _httpContextAccessor,
            _tenantContext,
            _logger);

    #region HandledFlowType

    [Fact]
    public void HandledFlowType_ReturnsLogin()
    {
        _sut.HandledFlowType.Should().Be(FlowType.Login);
    }

    #endregion

    #region Auto-Join (Zero Memberships + Default Tenant Configured)

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsWithDefaultTenant_AutoJoinsAndIssuesToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupSuccessfulMembershipCreation();
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsWithDefaultTenant_CreatesMembershipInDefaultTenant()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupSuccessfulMembershipCreation();
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userTenantRepository.Received(1).AddAsync(
            Arg.Is<UserTenantDto>(ut =>
                ut.UserId == TestUserId &&
                ut.TenantId == TestTenantId1 &&
                ut.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsWithDefaultTenant_PointsTenantContextAtDefaultTenantBeforeCreatingMembership()
    {
        // Arrange — the real membership repository stamps the current tenant onto new rows,
        // so the context must name the default tenant when the membership is written
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        Guid? tenantContextAtInsert = null;
        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                tenantContextAtInsert = _tenantContext.TenantId;
                return OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0));
            });

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        tenantContextAtInsert.Should().Be(TestTenantId1);
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroActiveMembershipsButPreviouslyLeftDefaultTenant_ReactivatesInsteadOfCreating()
    {
        // Arrange — leaving a tenant keeps the membership row with IsActive = false, and a user has one
        // row per tenant, so inserting a second one would violate the unique index
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupInactiveMembershipInDefaultTenant();
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();
        _userTenantRepository.ReactivateMembershipAsync(TestUserId, TestTenantId1, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        await _userTenantRepository.Received(1).ReactivateMembershipAsync(
            TestUserId, TestTenantId1, Arg.Any<CancellationToken>());
        await _userTenantRepository.DidNotReceive().AddAsync(
            Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ReactivatingPreviousMembershipFails_ReturnsAutoJoinFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupInactiveMembershipInDefaultTenant();
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupSuccessfulTokenGeneration();
        _userTenantRepository.ReactivateMembershipAsync(TestUserId, TestTenantId1, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("A conflict occurred while rejoining the tenant.", 409));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert — no token is issued for a membership that was not restored
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("AUTO_JOIN_FAILED");
        await _tokenService.DidNotReceive().GenerateTokenAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsNeverInDefaultTenant_StillCreatesNewMembership()
    {
        // Arrange — an old inactive membership in a DIFFERENT tenant must not block a fresh join
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>
            {
                new(Guid.NewGuid(), TestUserId, TestTenantId2, TestExternalUserId, MembershipStatus.Inactive)
            }));
        SetupDefaultTenantSlugSetting("default-org");
        SetupDefaultTenantLookup("default-org");
        SetupSuccessfulMembershipCreation();
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userTenantRepository.Received(1).AddAsync(
            Arg.Is<UserTenantDto>(m => m.TenantId == TestTenantId1), Arg.Any<CancellationToken>());
        await _userTenantRepository.DidNotReceive().ReactivateMembershipAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Tenantless Session (Zero Memberships + No Default Tenant)

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsNoDefaultTenant_IssuesTenantlessSession()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupNoDefaultTenantSlug();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsTenantless.Should().BeTrue();
        result.RequiresTenantSelection.Should().BeFalse();
        result.TenantList.Should().BeEmpty();
        result.Token.Should().BeNull();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestTenantlessToken);
        _authCookieWriter.Received(1).WriteIdTokenCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsEmptyDefaultTenantSlug_IssuesTenantlessSession()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();

        // Setting returns success but with whitespace value
        _settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("   "));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsTenantless.Should().BeTrue();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestTenantlessToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsNoDefaultTenant_TokenIsTenantlessWithAuthTimeAndNoTenantScopedToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupNoDefaultTenantSlug();
        IEnumerable<Claim>? captured = null;
        _tokenService.GenerateTenantlessTokenAsync(TestUserId, Arg.Do<IEnumerable<Claim>?>(c => captured = c))
            .Returns(TestTenantlessToken);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        var authTime = long.Parse(captured!.Single(c => c.Type == "auth_time").Value);
        authTime.Should().BeInRange(before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await _tokenService.DidNotReceive()
            .GenerateTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsNoDefaultTenant_TokenGenerationFails_ReturnsErrorAndWritesNoCookie()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupNoDefaultTenantSlug();
        _tokenService.GenerateTenantlessTokenAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns((string?)null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TOKEN_GENERATION_FAILED");
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ZeroMembershipsDefaultTenantConfiguredButNotFound_StaysAccessDenied()
    {
        // Arrange — a configured default that does not resolve is a configuration fault, not "no default"
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupDefaultTenantSlugSetting("missing-org");
        _tenantRepository.GetBySlugBypassFilterAsync("missing-org", Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.NotFound("Tenant not found"));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
        result.HttpStatus.Should().Be(403);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    #endregion

    #region Auto-Select (Single Membership)

    [Fact]
    public async Task HandleCallbackAsync_SingleMembership_AutoSelectsAndIssuesToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        result.RequiresTenantSelection.Should().BeFalse();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_SingleMembership_GeneratesTokenScopedToCorrectTenant()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId,
            TestTenantId1,
            Arg.Is<IEnumerable<Claim>>(claims =>
                claims.Any(c => c.Type == "auth_time")));
    }

    #endregion

    #region auth_time and the absolute session lifetime

    [Fact]
    public async Task HandleCallbackAsync_FreshLogin_TokenCarriesIdentityProviderAuthTime()
    {
        // Arrange — the user just typed their password
        var authTime = DateTimeOffset.UtcNow.AddSeconds(-5);
        SetupSuccessfulCodeExchange(authTime: authTime);
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId, TestTenantId1,
            Arg.Is<IEnumerable<Claim>>(c => AuthTimeOf(c) == authTime.ToUnixTimeSeconds()));
    }

    [Fact]
    public async Task HandleCallbackAsync_SilentLogin_KeepsTheOriginalAuthTime()
    {
        // Arrange — Keycloak signed the user in from its SSO session: auth_time is 3 hours old
        var authTime = DateTimeOffset.UtcNow.AddHours(-3);
        SetupSuccessfulCodeExchange(authTime: authTime);
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert — the session is not restarted
        result.IsSuccess.Should().BeTrue();
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId, TestTenantId1,
            Arg.Is<IEnumerable<Claim>>(c => AuthTimeOf(c) == authTime.ToUnixTimeSeconds()));
    }

    [Fact]
    public async Task HandleCallbackAsync_SilentLoginPastAbsoluteLifetime_Returns401ReauthRequiredAndWritesNoCookie()
    {
        // Arrange — the SSO session is 9 hours old; the absolute lifetime is 8 hours
        SetupSuccessfulCodeExchange(authTime: DateTimeOffset.UtcNow.AddHours(-9));
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.ReauthRequired);
        result.HttpStatus.Should().Be(401);
        await _tokenService.DidNotReceiveWithAnyArgs().GenerateTokenAsync(default, default, default);
        await _authCookieWriter.DidNotReceiveWithAnyArgs().WriteAuthCookieAsync(default!, default!);
    }

    [Fact]
    public async Task HandleCallbackAsync_TenantCapStricterThanSessionAge_Returns401ReauthRequired()
    {
        // Arrange — 2-hour-old SSO session; every lifetime in this test is 60 minutes
        _sut = CreateSut(absoluteLifetimeMinutes: 60);
        SetupSuccessfulCodeExchange(authTime: DateTimeOffset.UtcNow.AddHours(-2));
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert
        result.ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.ReauthRequired);
    }

    [Fact]
    public async Task HandleCallbackAsync_NoAuthTimeClaim_FallsBackToNow()
    {
        // Arrange — the identity provider did not send auth_time
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupSingleActiveMembership(TestTenantId1);
        SetupSuccessfulTokenGeneration();
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var result = await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId, TestTenantId1,
            Arg.Is<IEnumerable<Claim>>(c => AuthTimeOf(c) >= before));
    }

    [Fact]
    public async Task HandleCallbackAsync_TenantlessSession_CarriesIdentityProviderAuthTime()
    {
        // Arrange — no memberships and no default tenant: a tenantless session is issued
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-40);
        SetupSuccessfulCodeExchange(authTime: authTime);
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupZeroActiveMemberships();
        SetupNoDefaultTenantSlug();

        // Act
        await _sut.HandleCallbackAsync(CreateCallbackContext());

        // Assert
        await _tokenService.Received(1).GenerateTenantlessTokenAsync(
            TestUserId,
            Arg.Is<IEnumerable<Claim>>(c => AuthTimeOf(c) == authTime.ToUnixTimeSeconds()));
    }

    private static long AuthTimeOf(IEnumerable<Claim>? claims) =>
        long.Parse(claims!.Single(c => c.Type == "auth_time").Value, System.Globalization.CultureInfo.InvariantCulture);

    #endregion

    #region Multi-Membership (Tenant Selection Required, Excludes Enterprise)

    [Fact]
    public async Task HandleCallbackAsync_MultipleMemberships_ReturnsTenantSelectionRequired()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships();
        SetupTenantDetailsForMultipleMemberships();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RequiresTenantSelection.Should().BeTrue();
        result.TenantList.Should().NotBeNull();
        result.Token.Should().BeNull();
    }

    [Fact]
    public async Task HandleCallbackAsync_MultipleMemberships_ExcludesEnterpriseTenants()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships(); // 3 memberships: tenant1, tenant2 (standard), tenant3 (enterprise)
        SetupTenantDetailsForMultipleMemberships(); // tenant3 has RealmName set

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.TenantList.Should().HaveCount(2);
        result.TenantList!.Select(t => t.Id).Should().Contain(TestTenantId1);
        result.TenantList!.Select(t => t.Id).Should().Contain(TestTenantId2);
        result.TenantList!.Select(t => t.Id).Should().NotContain(TestTenantId3);
    }

    #endregion

    #region Host-Pin with Member → Token Issued

    [Fact]
    public async Task HandleCallbackAsync_HostPinnedWithMember_IssuesTokenForHostTenant()
    {
        // Arrange
        var hostTenant = CreateStandardTenantDto(TestTenantId1, "acme", "Acme Corp");
        var context = CreateCallbackContext(hostResolvedTenant: hostTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupActiveMembershipsIncluding(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        result.RequiresTenantSelection.Should().BeFalse();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_HostPinnedWithMember_GeneratesTokenScopedToHostTenant()
    {
        // Arrange
        var hostTenant = CreateStandardTenantDto(TestTenantId1, "acme", "Acme Corp");
        var context = CreateCallbackContext(hostResolvedTenant: hostTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupActiveMembershipsIncluding(TestTenantId1);
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId,
            TestTenantId1,
            Arg.Any<IEnumerable<Claim>?>());
    }

    #endregion

    #region Host-Pin with Non-Member → Access Denied

    [Fact]
    public async Task HandleCallbackAsync_HostPinnedWithNonMember_ReturnsAccessDenied()
    {
        // Arrange
        var hostTenant = CreateStandardTenantDto(TestTenantId1, "acme", "Acme Corp");
        var context = CreateCallbackContext(hostResolvedTenant: hostTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        // User has membership in tenant2, not in host-resolved tenant1
        SetupActiveMembershipsExcluding(TestTenantId1);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
        result.HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task HandleCallbackAsync_HostPinnedWithNonMember_DoesNotWriteCookie()
    {
        // Arrange
        var hostTenant = CreateStandardTenantDto(TestTenantId1, "acme", "Acme Corp");
        var context = CreateCallbackContext(hostResolvedTenant: hostTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupActiveMembershipsExcluding(TestTenantId1);

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    #endregion

    #region Enterprise Tenant → Not Yet Implemented

    [Fact]
    public async Task HandleCallbackAsync_EnterpriseTenantHostResolved_ReturnsNotYetImplemented()
    {
        // Arrange
        var enterpriseTenant = CreateEnterpriseTenantDto(TestTenantId1, "enterprise", "Enterprise Corp", "enterprise-realm");
        var context = CreateCallbackContext(hostResolvedTenant: enterpriseTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_YET_IMPLEMENTED");
        result.HttpStatus.Should().Be(501);
    }

    [Fact]
    public async Task HandleCallbackAsync_EnterpriseTenantHostResolved_DoesNotQueryMemberships()
    {
        // Arrange
        var enterpriseTenant = CreateEnterpriseTenantDto(TestTenantId1, "enterprise", "Enterprise Corp", "enterprise-realm");
        var context = CreateCallbackContext(hostResolvedTenant: enterpriseTenant);
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userTenantRepository.DidNotReceive()
            .GetAllMembershipsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Tenantless Token During Pending-Selection

    [Fact]
    public async Task HandleCallbackAsync_PendingSelection_WritesTenantlessGroundUpTokenNotKeycloakToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships();
        SetupTenantDetailsForMultipleMemberships();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert — the cookie holds a GroundUp tenantless token; the Keycloak access token is never stored
        result.IsTenantless.Should().BeTrue();
        result.RequiresTenantSelection.Should().BeTrue();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestTenantlessToken);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestAccessToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_PendingSelection_WritesIdTokenCookieForLogout()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships();
        SetupTenantDetailsForMultipleMemberships();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        _authCookieWriter.Received(1).WriteIdTokenCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCallbackAsync_PendingSelection_DoesNotGenerateTenantScopedToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships();
        SetupTenantDetailsForMultipleMemberships();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert — no tenant-scoped token should be generated
        await _tokenService.DidNotReceive()
            .GenerateTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>());
        await _tokenService.Received(1)
            .GenerateTenantlessTokenAsync(TestUserId, Arg.Any<IEnumerable<Claim>?>());
    }

    [Fact]
    public async Task HandleCallbackAsync_PendingSelection_TokenGenerationFails_ReturnsErrorAndWritesNoCookie()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupMultipleActiveMemberships();
        SetupTenantDetailsForMultipleMemberships();
        _tokenService.GenerateTenantlessTokenAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns((string?)null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TOKEN_GENERATION_FAILED");
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    #endregion

    #region Helper Methods

    private static FlowCallbackContext CreateCallbackContext(TenantDto? hostResolvedTenant = null)
    {
        var consumedState = new AuthFlowStateDto(
            Id: TestFlowStateId,
            FlowType: FlowType.Login,
            Status: FlowStatus.Consumed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: "state-token",
            CodeVerifier: TestCodeVerifier,
            RedirectUri: TestRedirectUri,
            OrganizationName: null,
            Nonce: TestNonce,
            CreatedByIp: "192.168.1.1",
            CreatedByUserAgent: "TestBrowser/1.0",
            ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            ConsumedAt: DateTime.UtcNow,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);

        return new FlowCallbackContext(
            AuthorizationCode: TestAuthCode,
            CodeVerifier: TestCodeVerifier,
            RedirectUri: TestRedirectUri,
            ConsumedState: consumedState,
            HostResolvedTenant: hostResolvedTenant);
    }

    private void SetupSuccessfulCodeExchange(string? idTokenNonce = null, DateTimeOffset? authTime = null)
    {
        var nonce = idTokenNonce ?? TestNonce;
        var idToken = CreateIdTokenWithNonce(nonce, authTime);

        var tokenResponse = new TokenResponseDto(
            AccessToken: TestAccessToken,
            RefreshToken: "refresh-token",
            ExpiresIn: 3600,
            IdToken: idToken);

        _identityProviderService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(tokenResponse);
    }

    private void SetupSuccessfulUserInfo()
    {
        var userInfo = new ExternalUserInfo(
            ExternalUserId: TestExternalUserId,
            Email: TestEmail,
            DisplayName: TestDisplayName,
            Attributes: null);

        _identityProviderService.GetUserInfoAsync(Arg.Any<string>())
            .Returns(userInfo);
    }

    private void SetupExistingUser()
    {
        var user = new UserDto(
            Id: TestUserId,
            ExternalUserId: TestExternalUserId,
            Email: TestEmail,
            DisplayName: TestDisplayName,
            IsActive: true);

        _userRepository.GetByExternalUserIdAsync(TestExternalUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));
    }

    /// <summary>
    /// The user once belonged to the default tenant (<c>TestTenantId1</c>) and left, so the row
    /// still exists but is inactive. The user has no active memberships.
    /// </summary>
    private void SetupInactiveMembershipInDefaultTenant()
    {
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>
            {
                new(Guid.NewGuid(), TestUserId, TestTenantId1, TestExternalUserId, MembershipStatus.Inactive)
            }));
    }

    private void SetupZeroActiveMemberships()
    {
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>()));
    }

    private void SetupSingleActiveMembership(Guid tenantId)
    {
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, tenantId, TestExternalUserId, Status: MembershipStatus.Active)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
    }

    private void SetupMultipleActiveMemberships()
    {
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, TestTenantId1, TestExternalUserId, Status: MembershipStatus.Active),
            new(Guid.NewGuid(), TestUserId, TestTenantId2, TestExternalUserId, Status: MembershipStatus.Active),
            new(Guid.NewGuid(), TestUserId, TestTenantId3, TestExternalUserId, Status: MembershipStatus.Active)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
    }

    private void SetupActiveMembershipsIncluding(Guid includedTenantId)
    {
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, includedTenantId, TestExternalUserId, Status: MembershipStatus.Active),
            new(Guid.NewGuid(), TestUserId, TestTenantId2, TestExternalUserId, Status: MembershipStatus.Active)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
    }

    private void SetupActiveMembershipsExcluding(Guid excludedTenantId)
    {
        // Create memberships that do NOT include the specified tenant
        var otherTenantId = excludedTenantId == TestTenantId2 ? TestTenantId3 : TestTenantId2;
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, otherTenantId, TestExternalUserId, Status: MembershipStatus.Active)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
    }

    private void SetupTenantDetailsForMultipleMemberships()
    {
        // Tenant1 and Tenant2 are standard (RealmName = null), Tenant3 is enterprise
        var tenants = new List<TenantDto>
        {
            CreateStandardTenantDto(TestTenantId1, "org-one", "Org One"),
            CreateStandardTenantDto(TestTenantId2, "org-two", "Org Two"),
            CreateEnterpriseTenantDto(TestTenantId3, "enterprise-org", "Enterprise Org", "enterprise-realm")
        };

        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(tenants));
    }

    private void SetupDefaultTenantSlugSetting(string slug)
    {
        _settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok(slug));
    }

    private void SetupNoDefaultTenantSlug()
    {
        _settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Setting not found", 404));
    }

    private void SetupDefaultTenantLookup(string slug)
    {
        var tenant = CreateStandardTenantDto(TestTenantId1, slug, "Default Org");

        _tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(tenant));
    }

    private void SetupSuccessfulMembershipCreation()
    {
        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0)));
    }

    private void SetupDefaultRoleNotConfigured()
    {
        _settingsService.GetAsync<string>("auth.application.default-role", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Setting not found", 404));
    }

    private void SetupSuccessfulTokenGeneration()
    {
        _tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns(TestGeneratedToken);
    }

    private static TenantDto CreateStandardTenantDto(Guid id, string slug, string name)
    {
        return new TenantDto(
            Id: id,
            Name: name,
            Slug: slug,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: true);
    }

    private static TenantDto CreateEnterpriseTenantDto(Guid id, string slug, string name, string realmName)
    {
        return new TenantDto(
            Id: id,
            Name: name,
            Slug: slug,
            TenantType: TenantType.Enterprise,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: realmName,
            IsActive: true);
    }

    /// <summary>
    /// Creates a minimal JWT id_token with the specified nonce claim in the payload.
    /// </summary>
    private static string CreateIdTokenWithNonce(string nonce, DateTimeOffset? authTime = null)
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var authTimeJson = authTime is { } t ? $",\"auth_time\":{t.ToUnixTimeSeconds()}" : string.Empty;
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{{\"nonce\":\"{nonce}\",\"sub\":\"test\"{authTimeJson}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var signature = "fake-signature";

        return $"{header}.{payload}.{signature}";
    }

    #endregion
}
