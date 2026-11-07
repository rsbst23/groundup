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

public sealed class SimpleSignupFlowHandlerTests
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
    private readonly ILogger<SimpleSignupFlowHandler> _logger;
    private readonly SimpleSignupFlowHandler _sut;

    // Reusable test data
    private const string TestAuthCode = "auth-code-signup-123";
    private const string TestCodeVerifier = "code-verifier-0123456789012345678901234567890123456789";
    private const string TestRedirectUri = "https://app.example.com/auth/callback";
    private const string TestNonce = "expected-nonce-value";
    private const string TestAccessToken = "keycloak-access-token-abc";
    private const string TestExternalUserId = "keycloak-sub-88888";
    private const string TestEmail = "newuser@example.com";
    private const string TestDisplayName = "New User";
    private const string TestGeneratedToken = "groundup-jwt-token-xyz";

    private static readonly Guid TestFlowStateId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestDefaultTenantId = Guid.NewGuid();

    public SimpleSignupFlowHandlerTests()
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
        _logger = Substitute.For<ILogger<SimpleSignupFlowHandler>>();

        var httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _sut = new SimpleSignupFlowHandler(
            _identityProviderService,
            _userRepository,
            _userTenantRepository,
            _tenantRepository,
            _tokenService,
            _authCookieWriter,
            _authFlowStateService,
            _settingsService,
            _httpContextAccessor,
            _logger);
    }

    #region HandledFlowType

    [Fact]
    public void HandledFlowType_ReturnsSimpleSignup()
    {
        _sut.HandledFlowType.Should().Be(FlowType.SimpleSignup);
    }

    #endregion

    #region Happy Path: New User, Default Tenant Configured

    [Fact]
    public async Task HandleCallbackAsync_NewUserWithDefaultTenant_CreatesMembershipAndIssuesToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupNewUserCreation();
        SetupDefaultTenantSlugSetting("community");
        SetupDefaultTenantLookup("community");
        SetupZeroActiveMemberships();
        SetupSuccessfulMembershipCreation();
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        _authCookieWriter.Received(1).WriteAuthCookie(Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_NewUser_CreatesMembershipInDefaultTenant()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupNewUserCreation();
        SetupDefaultTenantSlugSetting("community");
        SetupDefaultTenantLookup("community");
        SetupZeroActiveMemberships();
        SetupSuccessfulMembershipCreation();
        SetupDefaultRoleNotConfigured();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userTenantRepository.Received(1).AddAsync(
            Arg.Is<UserTenantDto>(ut =>
                ut.UserId == TestUserId &&
                ut.TenantId == TestDefaultTenantId &&
                ut.IsActive),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Happy Path: Existing User Already a Member (Idempotent)

    [Fact]
    public async Task HandleCallbackAsync_ExistingUserAlreadyMember_IssuesTokenWithoutCreatingMembership()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupDefaultTenantSlugSetting("community");
        SetupDefaultTenantLookup("community");
        SetupExistingMembershipInDefaultTenant();
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
        await _userTenantRepository.DidNotReceive().AddAsync(
            Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Default Tenant Not Configured

    [Fact]
    public async Task HandleCallbackAsync_NoDefaultTenantConfigured_ReturnsNoDefaultTenantError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupNoDefaultTenantSlug();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_DEFAULT_TENANT");
    }

    [Fact]
    public async Task HandleCallbackAsync_EmptyDefaultTenantSlug_ReturnsNoDefaultTenantError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();

        _settingsService.GetAsync<string>("auth.application.default-tenant-slug", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("   "));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_DEFAULT_TENANT");
    }

    #endregion

    #region Default Tenant Inactive

    [Fact]
    public async Task HandleCallbackAsync_DefaultTenantInactive_ReturnsNoDefaultTenantError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupExistingUser();
        SetupDefaultTenantSlugSetting("community");

        // Tenant exists but is inactive
        var inactiveTenant = new TenantDto(
            Id: TestDefaultTenantId,
            Name: "Community",
            Slug: "community",
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: false);

        _tenantRepository.GetBySlugBypassFilterAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<TenantDto>.Ok(inactiveTenant));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_DEFAULT_TENANT");
    }

    #endregion

    #region Code Exchange Failure

    [Fact]
    public async Task HandleCallbackAsync_CodeExchangeFails_ReturnsCodeExchangeFailedError()
    {
        // Arrange
        var context = CreateCallbackContext();

        _identityProviderService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((TokenResponseDto?)null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CODE_EXCHANGE_FAILED");
    }

    #endregion

    #region Nonce Mismatch

    [Fact]
    public async Task HandleCallbackAsync_NonceMismatch_ReturnsNonceMismatchError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupCodeExchangeWithWrongNonce();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NONCE_MISMATCH");
    }

    #endregion

    #region Userinfo Failure

    [Fact]
    public async Task HandleCallbackAsync_UserinfoFails_ReturnsUserinfoFailedError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();

        _identityProviderService.GetUserInfoAsync(Arg.Any<string>())
            .Returns((ExternalUserInfo?)null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("USERINFO_FAILED");
    }

    #endregion

    #region Helper Methods

    private static FlowCallbackContext CreateCallbackContext()
    {
        var consumedState = new AuthFlowStateDto(
            Id: TestFlowStateId,
            FlowType: FlowType.SimpleSignup,
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
            HostResolvedTenant: null);
    }

    private void SetupSuccessfulCodeExchange()
    {
        var idToken = CreateIdTokenWithNonce(TestNonce);
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

    private void SetupCodeExchangeWithWrongNonce()
    {
        var idToken = CreateIdTokenWithNonce("wrong-nonce-value");
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

    private void SetupNewUserCreation()
    {
        // No existing user
        _userRepository.GetByExternalUserIdAsync(TestExternalUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Fail("Not found", 404));

        // Create succeeds
        var newUser = new UserDto(
            Id: TestUserId,
            ExternalUserId: TestExternalUserId,
            Email: TestEmail,
            DisplayName: TestDisplayName,
            IsActive: true);

        _userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(newUser));
    }

    private void SetupZeroActiveMemberships()
    {
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>()));
    }

    private void SetupExistingMembershipInDefaultTenant()
    {
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, TestDefaultTenantId, TestExternalUserId, IsActive: true)
        };

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));
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
        var tenant = new TenantDto(
            Id: TestDefaultTenantId,
            Name: "Community",
            Slug: slug,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: true);

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

    /// <summary>
    /// Creates a minimal JWT id_token with the specified nonce claim in the payload.
    /// </summary>
    private static string CreateIdTokenWithNonce(string nonce)
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{{\"nonce\":\"{nonce}\",\"sub\":\"test\"}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var signature = "fake-signature";

        return $"{header}.{payload}.{signature}";
    }

    #endregion
}
