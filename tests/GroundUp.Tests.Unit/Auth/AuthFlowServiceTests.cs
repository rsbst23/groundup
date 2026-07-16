using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

public sealed class AuthFlowServiceTests
{
    private readonly IAuthUrlBuilder _authUrlBuilder;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly IAuthFlowStateRepository _authFlowStateRepository;
    private readonly HostResolvedTenant _hostResolvedTenant;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<AuthFlowService> _logger;
    private readonly AuthFlowService _sut;

    // Reusable test data
    private static readonly Guid TestFlowStateId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private const string TestStateToken = "test-state-token-abc123";
    private const string TestNonce = "test-nonce-xyz789";
    private const string TestCodeVerifier = "test-code-verifier-01234567890123456789012345678901234567890";
    private const string TestRedirectUri = "https://app.example.com/auth/callback";
    private const string TestAuthUrl = "https://keycloak.example.com/realms/shared/protocol/openid-connect/auth?state=abc";

    public AuthFlowServiceTests()
    {
        _authUrlBuilder = Substitute.For<IAuthUrlBuilder>();
        _authFlowStateService = Substitute.For<IAuthFlowStateService>();
        _authFlowStateRepository = Substitute.For<IAuthFlowStateRepository>();
        _hostResolvedTenant = new HostResolvedTenant();
        _logger = Substitute.For<ILogger<AuthFlowService>>();

        _authOptions = new AuthOptions
        {
            StateCookieName = "AuthState",
            FlowStateExpirationMinutes = 10,
            CallbackPath = "/auth/callback"
        };

        _sut = CreateSut();
    }

    #region InitiateFlowAsync Tests

    [Fact]
    public async Task InitiateFlowAsync_ValidRequest_CreatesStateWithAllFieldsPopulated()
    {
        // Arrange
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();

        await _authFlowStateService.Received(1).InitiateAsync(
            Arg.Is<InitiateAuthFlowRequest>(r =>
                r.FlowType == FlowType.Login &&
                r.StateToken == TestStateToken &&
                r.Nonce == TestNonce &&
                r.CodeVerifier == TestCodeVerifier &&
                r.RedirectUri == TestRedirectUri &&
                r.CreatedByIp != null &&
                r.CreatedByUserAgent != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateFlowAsync_NewOrganizationFlow_StoresOrganizationName()
    {
        // Arrange
        var request = new FlowInitiationRequest(
            FlowType.NewOrganization,
            OrganizationName: "Acme Corp");
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();

        await _authFlowStateService.Received(1).InitiateAsync(
            Arg.Is<InitiateAuthFlowRequest>(r =>
                r.OrganizationName == "Acme Corp" &&
                r.FlowType == FlowType.NewOrganization),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateFlowAsync_HostResolvedTenantPresent_StoresTenantId()
    {
        // Arrange
        _hostResolvedTenant.Tenant = CreateStandardTenant();
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();

        await _authFlowStateService.Received(1).InitiateAsync(
            Arg.Is<InitiateAuthFlowRequest>(r => r.TenantId == TestTenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateFlowAsync_EnterpriseTenantWithRealmName_StoresRealm()
    {
        // Arrange
        _hostResolvedTenant.Tenant = CreateEnterpriseTenant("bigco-realm");
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();

        await _authFlowStateService.Received(1).InitiateAsync(
            Arg.Is<InitiateAuthFlowRequest>(r => r.Realm == "bigco-realm"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateFlowAsync_Success_SetsStateCookieInResponse()
    {
        // Arrange
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("AuthState=" + TestStateToken);
        setCookieHeader.Should().Contain("httponly");
        setCookieHeader.Should().Contain("secure");
        setCookieHeader.Should().Contain("path=/");
    }

    [Fact]
    public async Task InitiateFlowAsync_Success_ReturnsRedirectUrlFromAuthUrlBuilder()
    {
        // Arrange
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();
        SetupSuccessfulUrlBuild();
        SetupSuccessfulStateCreation();

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.RedirectUrl.Should().Be(TestAuthUrl);
        result.Data!.FlowStateId.Should().Be(TestFlowStateId);
    }

    [Fact]
    public async Task InitiateFlowAsync_AuthUrlBuilderFails_ReturnsFailure()
    {
        // Arrange
        var request = new FlowInitiationRequest(FlowType.Login);
        var httpContext = CreateHttpContext();

        _authUrlBuilder.BuildAuthorizationUrlAsync(
                Arg.Any<AuthUrlRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthUrlResult>.Fail(
                "PublicBaseUrl is not configured",
                400,
                "MISSING_CONFIGURATION"));

        // Act
        var result = await _sut.InitiateFlowAsync(request, httpContext);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_CONFIGURATION");
    }

    #endregion

    #region HandleCallbackAsync Tests

    [Fact]
    public async Task HandleCallbackAsync_MissingStateCookie_ReturnsCsrfMismatchWith403()
    {
        // Arrange
        var httpContext = CreateHttpContext(); // no cookie set

        // Act
        var result = await _sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue(); // OperationResult is Ok, but FlowResult is an error
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("CSRF_STATE_MISMATCH");
        flowResult.HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task HandleCallbackAsync_MismatchedStateCookie_ReturnsCsrfMismatchWith403()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie("different-state-value");

        // Act
        var result = await _sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("CSRF_STATE_MISMATCH");
        flowResult.HttpStatus.Should().Be(403);
    }

    [Fact]
    public async Task HandleCallbackAsync_ValidCookieButUnknownStateToken_ReturnsStateNotFoundWith404()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Fail("Not found", 404));

        // Act
        var result = await _sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("STATE_NOT_FOUND");
        flowResult.HttpStatus.Should().Be(404);
    }

    [Fact]
    public async Task HandleCallbackAsync_AlreadyConsumedState_ReturnsFlowAlreadyConsumedWith410()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var consumedState = CreateFlowStateDto(FlowStatus.Consumed);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        // Act
        var result = await _sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("FLOW_ALREADY_CONSUMED");
        flowResult.HttpStatus.Should().Be(410);
    }

    [Fact]
    public async Task HandleCallbackAsync_ExpiredState_ReturnsFlowExpiredWith400()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var expiredState = CreateFlowStateDto(FlowStatus.Expired);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(expiredState));

        // Act
        var result = await _sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("FLOW_EXPIRED");
        flowResult.HttpStatus.Should().Be(400);
    }

    [Fact]
    public async Task HandleCallbackAsync_ValidState_RoutesToCorrectHandlerByFlowType()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var pendingState = CreateFlowStateDto(FlowStatus.Pending, FlowType.Login);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(pendingState));

        var consumedState = pendingState with { Status = FlowStatus.Consumed };
        _authFlowStateService.ConsumeAsync(pendingState.Id, FlowType.Login, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        var handler = Substitute.For<IFlowHandler>();
        handler.HandledFlowType.Returns(FlowType.Login);
        handler.HandleCallbackAsync(Arg.Any<FlowCallbackContext>())
            .Returns(FlowResult.Success("jwt-token-123"));

        // Recreate SUT with handler registered
        var sut = CreateSut(new[] { handler });

        // Act
        var result = await sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.IsSuccess.Should().BeTrue();
        result.Data!.Token.Should().Be("jwt-token-123");

        await handler.Received(1).HandleCallbackAsync(Arg.Any<FlowCallbackContext>());
    }

    [Fact]
    public async Task HandleCallbackAsync_NoHandlerRegistered_ReturnsNoHandlerError()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var pendingState = CreateFlowStateDto(FlowStatus.Pending, FlowType.Invitation);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(pendingState));

        var consumedState = pendingState with { Status = FlowStatus.Consumed };
        _authFlowStateService.ConsumeAsync(pendingState.Id, FlowType.Invitation, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        // SUT has no handlers registered (empty)
        var sut = CreateSut(Array.Empty<IFlowHandler>());

        // Act
        var result = await sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        var flowResult = result.Data!;
        flowResult.IsSuccess.Should().BeFalse();
        flowResult.ErrorCode.Should().Be("NO_HANDLER");
        flowResult.HttpStatus.Should().Be(500);
    }

    [Fact]
    public async Task HandleCallbackAsync_ValidState_PassesCorrectContextToHandler()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var pendingState = CreateFlowStateDto(FlowStatus.Pending, FlowType.NewOrganization);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(pendingState));

        var consumedState = pendingState with { Status = FlowStatus.Consumed };
        _authFlowStateService.ConsumeAsync(pendingState.Id, FlowType.NewOrganization, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        FlowCallbackContext? capturedContext = null;
        var handler = Substitute.For<IFlowHandler>();
        handler.HandledFlowType.Returns(FlowType.NewOrganization);
        handler.HandleCallbackAsync(Arg.Do<FlowCallbackContext>(ctx => capturedContext = ctx))
            .Returns(FlowResult.Success("token"));

        // Set a host-resolved tenant
        var tenant = CreateStandardTenant();
        _hostResolvedTenant.Tenant = tenant;

        var sut = CreateSut(new[] { handler });

        // Act
        await sut.HandleCallbackAsync("the-auth-code", TestStateToken, httpContext);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.AuthorizationCode.Should().Be("the-auth-code");
        capturedContext.CodeVerifier.Should().Be(consumedState.CodeVerifier);
        capturedContext.RedirectUri.Should().Be(consumedState.RedirectUri);
        capturedContext.ConsumedState.Should().Be(consumedState);
        capturedContext.HostResolvedTenant.Should().Be(tenant);
    }

    [Fact]
    public async Task HandleCallbackAsync_SuccessfulConsumption_ClearsStateCookie()
    {
        // Arrange
        var httpContext = CreateHttpContextWithStateCookie(TestStateToken);
        var pendingState = CreateFlowStateDto(FlowStatus.Pending, FlowType.Login);

        _authFlowStateRepository.FindByStateTokenAsync(TestStateToken, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(pendingState));

        var consumedState = pendingState with { Status = FlowStatus.Consumed };
        _authFlowStateService.ConsumeAsync(pendingState.Id, FlowType.Login, Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(consumedState));

        var handler = Substitute.For<IFlowHandler>();
        handler.HandledFlowType.Returns(FlowType.Login);
        handler.HandleCallbackAsync(Arg.Any<FlowCallbackContext>())
            .Returns(FlowResult.Success("token"));

        var sut = CreateSut(new[] { handler });

        // Act
        await sut.HandleCallbackAsync("auth-code", TestStateToken, httpContext);

        // Assert — the response should have a cookie deletion (expired cookie)
        var setCookieHeaders = httpContext.Response.Headers.SetCookie.ToArray();
        // Cookie deletion sets an expires in the past or uses Delete method
        // The implementation calls httpContext.Response.Cookies.Delete which sets expiry to epoch
        setCookieHeaders.Should().Contain(h => h!.Contains("AuthState") && h.Contains("expires="));
    }

    #endregion

    #region Helper Methods

    private AuthFlowService CreateSut(IEnumerable<IFlowHandler>? handlers = null)
    {
        return new AuthFlowService(
            _authUrlBuilder,
            _authFlowStateService,
            _authFlowStateRepository,
            handlers ?? Array.Empty<IFlowHandler>(),
            _hostResolvedTenant,
            Options.Create(_authOptions),
            _logger);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.com");
        context.Request.Headers["User-Agent"] = "TestBrowser/1.0";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");
        return context;
    }

    private DefaultHttpContext CreateHttpContextWithStateCookie(string stateValue)
    {
        var context = CreateHttpContext();
        context.Request.Headers["Cookie"] = $"{_authOptions.StateCookieName}={stateValue}";
        return context;
    }

    private void SetupSuccessfulUrlBuild()
    {
        var urlResult = new AuthUrlResult(
            AuthorizationUrl: TestAuthUrl,
            StateToken: TestStateToken,
            Nonce: TestNonce,
            CodeVerifier: TestCodeVerifier,
            RedirectUri: TestRedirectUri);

        _authUrlBuilder.BuildAuthorizationUrlAsync(
                Arg.Any<AuthUrlRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthUrlResult>.Ok(urlResult));
    }

    private void SetupSuccessfulStateCreation()
    {
        var stateDto = CreateFlowStateDto(FlowStatus.Pending);

        _authFlowStateService.InitiateAsync(
                Arg.Any<InitiateAuthFlowRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<AuthFlowStateDto>.Ok(stateDto));
    }

    private static AuthFlowStateDto CreateFlowStateDto(
        FlowStatus status,
        FlowType flowType = FlowType.Login)
    {
        return new AuthFlowStateDto(
            Id: TestFlowStateId,
            FlowType: flowType,
            Status: status,
            TenantId: null,
            InvitationId: null,
            JoinLinkId: null,
            Realm: null,
            ReturnUrl: null,
            StateToken: TestStateToken,
            CodeVerifier: TestCodeVerifier,
            RedirectUri: TestRedirectUri,
            OrganizationName: null,
            Nonce: TestNonce,
            CreatedByIp: "192.168.1.1",
            CreatedByUserAgent: "TestBrowser/1.0",
            ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            ConsumedAt: status == FlowStatus.Consumed ? DateTime.UtcNow : null,
            TerminatedAt: null,
            FailureReason: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);
    }

    private static TenantDto CreateStandardTenant()
    {
        return new TenantDto(
            Id: TestTenantId,
            Name: "Test Tenant",
            Slug: "test-tenant",
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: true);
    }

    private static TenantDto CreateEnterpriseTenant(string realmName)
    {
        return new TenantDto(
            Id: TestTenantId,
            Name: "Enterprise Tenant",
            Slug: "enterprise-tenant",
            TenantType: TenantType.Enterprise,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: realmName,
            IsActive: true);
    }

    #endregion
}
