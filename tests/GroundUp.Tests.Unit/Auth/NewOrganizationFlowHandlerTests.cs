using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Unit tests for <see cref="NewOrganizationFlowHandler"/>. Organization creation is delegated to
/// <see cref="IOrganizationProvisioningService"/>, so these tests assert how the handler uses it.
/// The rows that provisioning creates are covered by <c>OrganizationProvisioningServiceTests</c>
/// and the integration tests.
/// </summary>
public sealed class NewOrganizationFlowHandlerTests
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationProvisioningService _organizationProvisioningService;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<NewOrganizationFlowHandler> _logger;
    private readonly NewOrganizationFlowHandler _sut;

    // Reusable test data
    private const string TestAuthCode = "auth-code-123";
    private const string TestCodeVerifier = "code-verifier-0123456789012345678901234567890123456789";
    private const string TestRedirectUri = "https://app.example.com/auth/callback";
    private const string TestNonce = "expected-nonce-value";
    private const string TestAccessToken = "access-token-abc";
    private const string TestExternalUserId = "keycloak-sub-12345";
    private const string TestEmail = "founder@acme.com";
    private const string TestDisplayName = "Jane Founder";
    private const string TestOrgName = "Acme Corp";
    private const string TestGeneratedToken = "groundup-jwt-token-xyz";

    private static readonly Guid TestFlowStateId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public NewOrganizationFlowHandlerTests()
    {
        _identityProviderService = Substitute.For<IIdentityProviderService>();
        _authFlowStateService = Substitute.For<IAuthFlowStateService>();
        _userRepository = Substitute.For<IUserRepository>();
        _organizationProvisioningService = Substitute.For<IOrganizationProvisioningService>();
        _tokenService = Substitute.For<ITokenService>();
        _authCookieWriter = Substitute.For<IAuthCookieWriter>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _tenantContext = new TenantContext();
        _logger = Substitute.For<ILogger<NewOrganizationFlowHandler>>();

        var httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _sut = new NewOrganizationFlowHandler(
            _identityProviderService,
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.DecodingIdTokenValidator(),
            GroundUp.Tests.Unit.Auth.TestHelpers.FlowHandlerTestDoubles.SessionGuard(),
            _authFlowStateService,
            _userRepository,
            _organizationProvisioningService,
            _tokenService,
            _authCookieWriter,
            _httpContextAccessor,
            _tenantContext,
            _logger);
    }

    #region HandledFlowType

    [Fact]
    public void HandledFlowType_ReturnsNewOrganization()
    {
        _sut.HandledFlowType.Should().Be(FlowType.NewOrganization);
    }

    #endregion

    #region Code Exchange Failure

    [Fact]
    public async Task HandleCallbackAsync_CodeExchangeReturnsNull_ReturnsCodeExchangeFailedError()
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

    [Fact]
    public async Task HandleCallbackAsync_CodeExchangeReturnsNull_MarksFlowAsFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        _identityProviderService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((TokenResponseDto?)null);

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _authFlowStateService.Received(1).MarkFailedAsync(
            TestFlowStateId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_CodeExchangeReturnsNull_DoesNotProvisionOrganization()
    {
        // Arrange
        var context = CreateCallbackContext();
        _identityProviderService.ExchangeCodeForTokensAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((TokenResponseDto?)null);

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _organizationProvisioningService.DidNotReceive().ProvisionAsync(
            Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Nonce Mismatch

    [Fact]
    public async Task HandleCallbackAsync_NonceMismatch_ReturnsNonceMismatchError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange(idTokenNonce: "wrong-nonce-value");

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NONCE_MISMATCH");
    }

    [Fact]
    public async Task HandleCallbackAsync_NonceMismatch_MarksFlowAsFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange(idTokenNonce: "wrong-nonce-value");

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _authFlowStateService.Received(1).MarkFailedAsync(
            TestFlowStateId, Arg.Is<string>(s => s.Contains("ID token")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_IdTokenNull_ReturnsIdTokenInvalidError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange(idToken: null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert — a missing ID token is not a nonce problem: there is nothing to validate
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(GroundUp.Auth.Services.IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task HandleCallbackAsync_NonceMismatch_DoesNotProvisionOrganization()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange(idTokenNonce: "wrong-nonce-value");

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _organizationProvisioningService.DidNotReceive().ProvisionAsync(
            Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region UserInfo Failure

    [Fact]
    public async Task HandleCallbackAsync_UserInfoReturnsNull_ReturnsUserInfoFailedError()
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

    [Fact]
    public async Task HandleCallbackAsync_UserInfoReturnsNull_MarksFlowAsFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        _identityProviderService.GetUserInfoAsync(Arg.Any<string>())
            .Returns((ExternalUserInfo?)null);

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _authFlowStateService.Received(1).MarkFailedAsync(
            TestFlowStateId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Successful Flow

    [Fact]
    public async Task HandleCallbackAsync_Success_ReturnsGeneratedToken()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_ProvisionsOrganizationForResolvedUser()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _organizationProvisioningService.Received(1).ProvisionAsync(
            Arg.Is<OrganizationProvisioningRequest>(r =>
                r.UserId == TestUserId &&
                r.ExternalUserId == TestExternalUserId &&
                r.OrganizationName == TestOrgName),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_NoOrganizationNameOnFlowState_ProvisionsWithFallbackName()
    {
        // Arrange
        var context = CreateCallbackContext(organizationName: null);
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _organizationProvisioningService.Received(1).ProvisionAsync(
            Arg.Is<OrganizationProvisioningRequest>(r => r.OrganizationName == "Organization"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_ResolvesUserByExternalUserId()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userRepository.Received(1).GetByExternalUserIdAsync(
            TestExternalUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_UserDoesNotExist_CreatesUserBeforeProvisioning()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userRepository.Received(1).AddAsync(
            Arg.Is<UserDto>(u =>
                u.ExternalUserId == TestExternalUserId &&
                u.Email == TestEmail &&
                u.DisplayName == TestDisplayName &&
                u.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_UserAlreadyExists_DoesNotCreateUserAgain()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        _userRepository.GetByExternalUserIdAsync(TestExternalUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(CreateExistingUserDto()));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.DidNotReceive().AddAsync(
            Arg.Any<UserDto>(), Arg.Any<CancellationToken>());
        await _organizationProvisioningService.Received(1).ProvisionAsync(
            Arg.Is<OrganizationProvisioningRequest>(r => r.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_GeneratesTokenForNewTenantWithAuthTimeClaim()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(
            TestUserId,
            TestTenantId,
            Arg.Is<IEnumerable<Claim>>(claims =>
                claims.Any(c => c.Type == "auth_time")));
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_PointsTenantContextAtNewTenant()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert — token generation resolves roles in the new tenant
        _tenantContext.TenantId.Should().Be(TestTenantId);
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_WritesAuthCookie()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        _authCookieWriter.Received(1).WriteAuthCookieAsync(
            Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_WritesIdTokenCookieForLogout()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        _authCookieWriter.Received(1).WriteIdTokenCookieAsync(
            Arg.Any<HttpContext>(), CreateIdTokenWithNonce(TestNonce));
    }

    #endregion

    #region Provisioning Failure

    [Fact]
    public async Task HandleCallbackAsync_ProvisioningFails_ReturnsErrorWithServiceErrorCodeAndStatus()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        SetupProvisioningFailure("Unable to create organization.", 409, "SLUG_COLLISION");

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SLUG_COLLISION");
        result.HttpStatus.Should().Be(409);
        result.ErrorMessage.Should().Be("Unable to create organization.");
    }

    [Fact]
    public async Task HandleCallbackAsync_ProvisioningFailsWithoutErrorCode_UsesGenericErrorCode()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        SetupProvisioningFailure("Something went wrong.", 500, errorCode: null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ORGANIZATION_PROVISIONING_FAILED");
    }

    [Fact]
    public async Task HandleCallbackAsync_ProvisioningFails_MarksFlowAsFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        SetupProvisioningFailure("Unable to create organization.", 409, "SLUG_COLLISION");

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _authFlowStateService.Received(1).MarkFailedAsync(
            TestFlowStateId, Arg.Is<string>(s => s.Contains("SLUG_COLLISION")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ProvisioningFails_DoesNotIssueTokenOrWriteCookies()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        SetupProvisioningFailure("Unable to create organization.", 409, "SLUG_COLLISION");

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tokenService.DidNotReceive().GenerateTokenAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>());
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        _authCookieWriter.DidNotReceive().WriteIdTokenCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleCallbackAsync_ProvisioningThrows_PropagatesAndDoesNotIssueToken()
    {
        // Arrange — the service throws when a step fails and the transaction rolls back
        var context = CreateCallbackContext();
        SetupHappyPath();
        _organizationProvisioningService.ProvisionAsync(
                Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Failed to create membership"));

        // Act
        Func<Task> act = () => _sut.HandleCallbackAsync(context);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        await _tokenService.DidNotReceive().GenerateTokenAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>());
    }

    #endregion

    #region Token Generation Failure

    [Fact]
    public async Task HandleCallbackAsync_TokenGenerationReturnsNull_ReturnsErrorAndMarksFlowFailed()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();
        _tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns((string?)null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TOKEN_GENERATION_FAILED");
        await _authFlowStateService.Received(1).MarkFailedAsync(
            TestFlowStateId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    #endregion

    #region User ExternalUserId Collision

    [Fact]
    public async Task HandleCallbackAsync_UserExternalUserIdCollision_ReReadsExistingUser()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupHappyPath();

        // First lookup finds nothing, the insert hits a unique constraint (concurrent creation),
        // the second lookup returns the user created by the other request.
        var getUserCallCount = 0;
        _userRepository.GetByExternalUserIdAsync(TestExternalUserId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                getUserCallCount++;
                return getUserCallCount == 1
                    ? OperationResult<UserDto>.NotFound("User not found")
                    : OperationResult<UserDto>.Ok(CreateExistingUserDto());
            });

        // BaseRepository.AddAsync reports a unique-constraint violation as a 409 result; it does not throw
        _userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Fail("A conflict occurred while saving the entity.", 409, ErrorCodes.Conflict));

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(2).GetByExternalUserIdAsync(
            TestExternalUserId, Arg.Any<CancellationToken>());
        await _organizationProvisioningService.Received(1).ProvisionAsync(
            Arg.Is<OrganizationProvisioningRequest>(r => r.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static FlowCallbackContext CreateCallbackContext(string? organizationName = TestOrgName)
    {
        var consumedState = new AuthFlowStateDto(
            Id: TestFlowStateId,
            FlowType: FlowType.NewOrganization,
            Status: FlowStatus.Consumed,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: "state-token",
            CodeVerifier: TestCodeVerifier,
            RedirectUri: TestRedirectUri,
            OrganizationName: organizationName,
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

    /// <summary>
    /// Configures every collaborator for a fully successful callback: code exchange, userinfo,
    /// a brand-new user, successful provisioning and token generation.
    /// </summary>
    private void SetupHappyPath()
    {
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupNewUser();
        SetupSuccessfulProvisioning();
        SetupSuccessfulTokenGeneration();
    }

    private void SetupSuccessfulCodeExchange(string? idTokenNonce = null, string? idToken = "placeholder")
    {
        var nonce = idTokenNonce ?? TestNonce;
        string? actualIdToken = idToken;

        if (actualIdToken == "placeholder")
        {
            actualIdToken = CreateIdTokenWithNonce(nonce);
        }

        var tokenResponse = new TokenResponseDto(
            AccessToken: TestAccessToken,
            RefreshToken: "refresh-token",
            ExpiresIn: 3600,
            IdToken: actualIdToken);

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

    private void SetupNewUser()
    {
        _userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound("Not found"));

        _userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<UserDto>(0);
                return OperationResult<UserDto>.Ok(dto with { Id = TestUserId });
            });
    }

    private void SetupSuccessfulProvisioning()
    {
        _organizationProvisioningService.ProvisionAsync(
                Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<OrganizationProvisioningResult>.Ok(
                new OrganizationProvisioningResult(TestTenantId, TestOrgName, "acme-corp")));
    }

    private void SetupProvisioningFailure(string message, int statusCode, string? errorCode)
    {
        _organizationProvisioningService.ProvisionAsync(
                Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<OrganizationProvisioningResult>.Fail(message, statusCode, errorCode));
    }

    private void SetupSuccessfulTokenGeneration()
    {
        _tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns(TestGeneratedToken);
    }

    private static UserDto CreateExistingUserDto()
    {
        return new UserDto(
            Id: TestUserId,
            ExternalUserId: TestExternalUserId,
            Email: TestEmail,
            DisplayName: TestDisplayName,
            IsActive: true);
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
