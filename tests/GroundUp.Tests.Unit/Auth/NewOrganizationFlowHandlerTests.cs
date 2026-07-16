using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Core;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Auth;

public sealed class NewOrganizationFlowHandlerTests
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
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
    private static readonly Guid TestRoleId = Guid.NewGuid();

    public NewOrganizationFlowHandlerTests()
    {
        _identityProviderService = Substitute.For<IIdentityProviderService>();
        _authFlowStateService = Substitute.For<IAuthFlowStateService>();
        _tenantRepository = Substitute.For<ITenantRepository>();
        _userRepository = Substitute.For<IUserRepository>();
        _userTenantRepository = Substitute.For<IUserTenantRepository>();
        _userRoleRepository = Substitute.For<IUserRoleRepository>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _tokenService = Substitute.For<ITokenService>();
        _authCookieWriter = Substitute.For<IAuthCookieWriter>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _logger = Substitute.For<ILogger<NewOrganizationFlowHandler>>();

        var httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _sut = new NewOrganizationFlowHandler(
            _identityProviderService,
            _authFlowStateService,
            _tenantRepository,
            _userRepository,
            _userTenantRepository,
            _userRoleRepository,
            _roleRepository,
            _unitOfWork,
            _tokenService,
            _authCookieWriter,
            _httpContextAccessor,
            new TenantContext(),
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
            TestFlowStateId, Arg.Is<string>(s => s.Contains("Nonce")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_IdTokenNull_ReturnsNonceMismatchError()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange(idToken: null);

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NONCE_MISMATCH");
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
    public async Task HandleCallbackAsync_Success_CreatesAllEntitiesAtomically()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupSuccessfulTransaction();
        SetupSuccessfulTokenGeneration();

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().Be(TestGeneratedToken);

        // Verify transaction was used
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_CreatesTenantWithDerivedSlug()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupTransactionThatExecutesDelegate();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tenantRepository.Received(1).AddAsync(
            Arg.Is<TenantDto>(t =>
                t.Slug == "acme-corp" &&
                t.Name == TestOrgName &&
                t.TenantType == TenantType.Standard &&
                t.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_ResolvesUserByExternalUserId()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupTransactionThatExecutesDelegate();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userRepository.Received(1).GetByExternalUserIdAsync(
            TestExternalUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_CreatesTenantAdminRole()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupTransactionThatExecutesDelegate();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _roleRepository.Received(1).AddAsync(
            Arg.Is<RoleDto>(r =>
                r.Name == AuthRoleNames.TenantAdmin &&
                r.IsSystem &&
                r.RoleType == RoleType.Application),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_AssignsTenantAdminToFoundingUser()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupTransactionThatExecutesDelegate();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _userRoleRepository.Received(1).AddAsync(
            Arg.Is<UserRoleDto>(ur => ur.UserId == TestUserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_WritesAuthCookie()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupSuccessfulTransaction();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        _authCookieWriter.Received(1).WriteAuthCookie(
            Arg.Any<HttpContext>(), TestGeneratedToken);
    }

    [Fact]
    public async Task HandleCallbackAsync_Success_GeneratesTokenWithAuthTimeClaim()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupSuccessfulTransaction();
        SetupSuccessfulTokenGeneration();

        // Act
        await _sut.HandleCallbackAsync(context);

        // Assert
        await _tokenService.Received(1).GenerateTokenAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Is<IEnumerable<Claim>>(claims =>
                claims.Any(c => c.Type == "auth_time")));
    }

    #endregion

    #region Slug Collision Retry

    [Fact]
    public async Task HandleCallbackAsync_SlugCollision_RetriesWithDisambiguatedSlug()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupSuccessfulTokenGeneration();

        // First attempt: tenant creation fails (slug collision)
        // Second attempt: succeeds
        var callCount = 0;
        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                var operation = callInfo.ArgAt<Func<CancellationToken, Task>>(0);
                if (callCount == 1)
                {
                    // Simulate slug collision by making tenant add fail
                    _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
                        .Returns(OperationResult<TenantDto>.Fail("Unique constraint violation", 409));
                    return Task.FromResult(OperationResult.Fail("Unique constraint violation", 409));
                }
                else
                {
                    // Second attempt succeeds
                    SetupTransactionDelegateForSuccess();
                    return InvokeTransactionDelegate(operation);
                }
            });

        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _unitOfWork.Received(2).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region User ExternalUserId Collision

    [Fact]
    public async Task HandleCallbackAsync_UserExternalUserIdCollision_ReReadsExistingUser()
    {
        // Arrange
        var context = CreateCallbackContext();
        SetupSuccessfulCodeExchange();
        SetupSuccessfulUserInfo();
        SetupSuccessfulTokenGeneration();

        // Setup: first GetByExternalUserIdAsync returns not found,
        // AddAsync throws unique constraint violation,
        // second GetByExternalUserIdAsync returns the existing user
        var getUserCallCount = 0;
        _userRepository.GetByExternalUserIdAsync(TestExternalUserId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                getUserCallCount++;
                if (getUserCallCount == 1)
                {
                    return OperationResult<UserDto>.NotFound("User not found");
                }
                return OperationResult<UserDto>.Ok(CreateExistingUserDto());
            });

        _userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("duplicate key value violates unique constraint \"23505\""));

        SetupTransactionThatExecutesDelegateWithUserCollision();
        
        // Act
        var result = await _sut.HandleCallbackAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Should have called GetByExternalUserIdAsync at least twice (initial lookup + re-read)
        await _userRepository.Received(2).GetByExternalUserIdAsync(
            TestExternalUserId, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helper Methods

    private static FlowCallbackContext CreateCallbackContext()
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
            OrganizationName: TestOrgName,
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

    private void SetupSuccessfulTransaction()
    {
        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
    }

    private void SetupTransactionThatExecutesDelegate()
    {
        // Setup repos to succeed inside the transaction
        SetupTransactionDelegateForSuccess();

        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => InvokeTransactionDelegate(
                callInfo.ArgAt<Func<CancellationToken, Task>>(0)));
    }

    private void SetupTransactionThatExecutesDelegateWithUserCollision()
    {
        // Tenant creation succeeds
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<TenantDto>(0);
                return OperationResult<TenantDto>.Ok(dto with { Id = TestTenantId });
            });

        // Role and membership succeed
        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<RoleDto>(0);
                return OperationResult<RoleDto>.Ok(dto with { Id = TestRoleId });
            });

        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0)));

        _userRoleRepository.AddAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserRoleDto>.Ok(callInfo.ArgAt<UserRoleDto>(0)));

        _unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => InvokeTransactionDelegate(
                callInfo.ArgAt<Func<CancellationToken, Task>>(0)));
    }

    private void SetupTransactionDelegateForSuccess()
    {
        _tenantRepository.AddAsync(Arg.Any<TenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<TenantDto>(0);
                return OperationResult<TenantDto>.Ok(dto with { Id = TestTenantId });
            });

        _userRepository.GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound("Not found"));

        _userRepository.AddAsync(Arg.Any<UserDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<UserDto>(0);
                return OperationResult<UserDto>.Ok(dto with { Id = TestUserId });
            });

        _userTenantRepository.AddAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserTenantDto>.Ok(callInfo.ArgAt<UserTenantDto>(0)));

        _roleRepository.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var dto = callInfo.ArgAt<RoleDto>(0);
                return OperationResult<RoleDto>.Ok(dto with { Id = TestRoleId });
            });

        _userRoleRepository.AddAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OperationResult<UserRoleDto>.Ok(callInfo.ArgAt<UserRoleDto>(0)));
    }

    private void SetupSuccessfulTokenGeneration()
    {
        _tokenService.GenerateTokenAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IEnumerable<Claim>?>())
            .Returns(TestGeneratedToken);
    }

    private static async Task<OperationResult> InvokeTransactionDelegate(
        Func<CancellationToken, Task> operation)
    {
        try
        {
            await operation(CancellationToken.None);
            return OperationResult.Ok();
        }
        catch
        {
            return OperationResult.Fail("Transaction failed", 500);
        }
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
