using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Api;
using GroundUp.Auth.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth;

public sealed class AuthControllerTests
{
    private readonly IAuthFlowService _authFlowService;
    private readonly IAuthSessionService _authSessionService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IUserRepository _userRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly LastAdminGuard _lastAdminGuard;
    private readonly AuthOptions _authOptions;
    private readonly KeycloakOptions _keycloakOptions;
    private readonly IAntiforgery _antiforgery;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IOrganizationProvisioningService _organizationProvisioningService =
        Substitute.For<IOrganizationProvisioningService>();
    private readonly AuthController _sut;

    // Reusable test data
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private const string TestToken = "test-jwt-token-abc123";
    private const string TestRedirectUrl = "https://keycloak.example.com/realms/shared/protocol/openid-connect/auth?state=abc";
    private static readonly Guid TestFlowStateId = Guid.NewGuid();

    public AuthControllerTests()
    {
        _authFlowService = Substitute.For<IAuthFlowService>();
        _authSessionService = Substitute.For<IAuthSessionService>();
        _authCookieWriter = Substitute.For<IAuthCookieWriter>();
        _userRepository = Substitute.For<IUserRepository>();
        _userTenantRepository = Substitute.For<IUserTenantRepository>();
        _tenantRepository = Substitute.For<ITenantRepository>();
        _userRoleRepository = Substitute.For<IUserRoleRepository>();
        _lastAdminGuard = new LastAdminGuard(_userRoleRepository);

        _authOptions = new AuthOptions
        {
            UserIdClaimType = "sub",
            EmailClaimType = "email",
            DisplayNameClaimType = "name",
            TenantIdClaimType = "tid",
            CookieName = "AuthToken"
        };

        _keycloakOptions = new KeycloakOptions
        {
            PublicBaseUrl = "https://keycloak.example.com",
            SharedRealmName = "shared",
            AppClientId = "groundup-app"
        };

        _antiforgery = Substitute.For<IAntiforgery>();

        _sut = CreateController();
    }

    #region POST /auth/login

    [Fact]
    public async Task Login_FlowServiceReturnsSuccess_Returns200WithRedirectUrl()
    {
        // Arrange
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        // Act
        var result = await _sut.Login();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["redirectUrl"].Should().Be(TestRedirectUrl);
    }

    [Fact]
    public async Task Login_FlowServiceReturnsFailure_ReturnsErrorStatusCode()
    {
        // Arrange
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Fail("Configuration missing", 500, "MISSING_CONFIG"));

        // Act
        var result = await _sut.Login();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region POST /auth/signup

    [Fact]
    public async Task Signup_WithoutOrganizationName_InitiatesFlowAndReturns200()
    {
        // Arrange — null organizationName means simple signup (uses Login flow type as placeholder)
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        // Act
        var result = await _sut.Signup(request: null);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["redirectUrl"].Should().Be(TestRedirectUrl);
    }

    [Fact]
    public async Task Signup_WithOrganizationNameExceeding200Chars_Returns400()
    {
        // Arrange
        var longName = new string('A', 201);
        var request = new SignupRequestDto(longName);

        // Act
        var result = await _sut.Signup(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Signup_WithValidOrganizationName_Returns200WithRedirectUrl()
    {
        // Arrange
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        var request = new SignupRequestDto("Acme Corp");

        // Act
        var result = await _sut.Signup(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["redirectUrl"].Should().Be(TestRedirectUrl);
    }

    [Fact]
    public async Task Signup_WithValidOrganizationName_PassesNewOrganizationFlowType()
    {
        // Arrange
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        var request = new SignupRequestDto("Acme Corp");

        // Act
        await _sut.Signup(request);

        // Assert
        await _authFlowService.Received(1).InitiateFlowAsync(
            Arg.Is<FlowInitiationRequest>(r =>
                r.FlowType == GroundUp.Auth.Core.Enums.FlowType.NewOrganization &&
                r.OrganizationName == "Acme Corp"),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Signup_FlowServiceReturnsFailure_ReturnsErrorStatusCode()
    {
        // Arrange
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Fail("Configuration missing", 500, "MISSING_CONFIG"));

        var request = new SignupRequestDto("Acme Corp");

        // Act
        var result = await _sut.Signup(request);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GET /auth/callback

    [Fact]
    public async Task Callback_MissingCodeParameter_Returns400()
    {
        // Act
        var result = await _sut.Callback(code: null, state: "some-state");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Callback_MissingStateParameter_Returns400()
    {
        // Act
        var result = await _sut.Callback(code: "some-code", state: null);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Callback_SuccessfulFlow_Returns200WithTokenAndRedirectUrl()
    {
        // Arrange
        var flowResult = FlowResult.Success(TestToken, "/dashboard");
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(flowResult));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["token"].Should().Be(TestToken);
        body["redirectUrl"].Should().Be("/dashboard");
    }

    [Fact]
    public async Task Callback_SuccessfulFlowWithNullRedirectUrl_Returns200WithDefaultRedirectUrl()
    {
        // Arrange
        var flowResult = FlowResult.Success(TestToken, redirectUrl: null);
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(flowResult));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["token"].Should().Be(TestToken);
        body["redirectUrl"].Should().Be("/");
    }

    [Fact]
    public async Task Callback_TenantSelectionRequired_Returns200WithTenantListAndFlag()
    {
        // Arrange
        var tenants = new List<TenantListItemDto>
        {
            new(Guid.NewGuid(), "Tenant A", "Description A"),
            new(Guid.NewGuid(), "Tenant B", null)
        };
        var flowResult = FlowResult.TenantSelectionRequired(tenants);
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(flowResult));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["requiresTenantSelection"].Should().Be(true);
        body["tenants"].Should().Be(tenants);
    }

    [Fact]
    public async Task Callback_FlowResultError_ReturnsAppropriateStatusCode()
    {
        // Arrange
        var flowResult = FlowResult.Error("ACCESS_DENIED", "User is not a member", 403);
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(flowResult));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Callback_OperationResultFailure_ReturnsErrorStatusCode()
    {
        // Arrange
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Fail("Internal error", 500));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region GET /auth/me

    [Fact]
    public void Me_Unauthenticated_Returns401()
    {
        // Arrange — controller has DefaultHttpContext with no authenticated user (default)

        // Act
        var result = _sut.Me();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void Me_PendingSelectionPrincipalNoTidClaim_ReturnsIdentityWithNullTenant()
    {
        // Arrange — Keycloak-token principal with sub but no tid
        var claims = new[]
        {
            new Claim("sub", "keycloak-sub-12345"),
            new Claim("email", "user@example.com"),
            new Claim("name", "Test User")
        };
        SetAuthenticatedUser(claims);

        // Act
        var result = _sut.Me();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["userId"].Should().Be("keycloak-sub-12345");
        body["email"].Should().Be("user@example.com");
        body["displayName"].Should().Be("Test User");
        body["tenantId"].Should().BeNull();
    }

    [Fact]
    public void Me_AuthenticatedGroundUpToken_ReturnsFullClaims()
    {
        // Arrange — GroundUp JWT with sub (userId GUID), tid, email, name, roles
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString()),
            new Claim("email", "admin@acme.com"),
            new Claim("name", "Admin User"),
            new Claim("role", "TenantAdmin"),
            new Claim("role", "ProjectManager")
        };
        SetAuthenticatedUser(claims);

        // Act
        var result = _sut.Me();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["userId"].Should().Be(TestUserId.ToString());
        body["tenantId"].Should().Be(TestTenantId);
        body["email"].Should().Be("admin@acme.com");
        body["displayName"].Should().Be("Admin User");

        var roles = body["roles"] as List<string>;
        roles.Should().NotBeNull();
        roles.Should().Contain("TenantAdmin");
        roles.Should().Contain("ProjectManager");
    }

    #endregion

    #region POST /auth/set-tenant

    [Fact]
    public async Task SetTenant_Unauthenticated_Returns401()
    {
        // Arrange — no authenticated user
        var request = new SetTenantRequestDto(TestTenantId);

        // Act
        var result = await _sut.SetTenant(request);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task SetTenant_KeycloakPrincipalWithSub_ResolvesUserByExternalUserId()
    {
        // Arrange — Keycloak-token principal (has sub, no tid)
        var externalSub = "keycloak|user-123";
        var claims = new[]
        {
            new Claim("sub", externalSub)
        };
        SetAuthenticatedExternalIdpUser(claims);

        var user = new UserDto(TestUserId, externalSub, "user@example.com", "Test User", true);
        _userRepository.GetByExternalUserIdAsync(externalSub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));

        var response = new SetTenantResponseDto(false, null, TestToken);
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(response));

        var request = new SetTenantRequestDto(TestTenantId);

        // Act
        var result = await _sut.SetTenant(request);

        // Assert
        await _userRepository.Received(1).GetByExternalUserIdAsync(externalSub, Arg.Any<CancellationToken>());
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SetTenant_UserNotMember_Returns403()
    {
        // Arrange — Keycloak-token principal (has sub, no tid)
        var externalSub = "keycloak|user-456";
        var claims = new[]
        {
            new Claim("sub", externalSub)
        };
        SetAuthenticatedExternalIdpUser(claims);

        var user = new UserDto(TestUserId, externalSub, "user@example.com", "Test User", true);
        _userRepository.GetByExternalUserIdAsync(externalSub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));

        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Forbidden("User does not belong to the specified tenant"));

        var request = new SetTenantRequestDto(TestTenantId);

        // Act
        var result = await _sut.SetTenant(request);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SetTenant_GroundUpTokenWithTid_PreservesAuthTime()
    {
        // Arrange — GroundUp token principal (has sub as GUID, tid, and auth_time)
        var authTimeUnix = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString()),
            new Claim("auth_time", authTimeUnix.ToString())
        };
        SetAuthenticatedUser(claims);

        var newTenantId = Guid.NewGuid();
        var response = new SetTenantResponseDto(false, null, TestToken);
        _authSessionService.SetTenantAsync(
                TestUserId,
                newTenantId,
                Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(response));

        var request = new SetTenantRequestDto(newTenantId);

        // Act
        var result = await _sut.SetTenant(request);

        // Assert
        result.Should().BeOfType<OkResult>();
        await _authSessionService.Received(1).SetTenantAsync(
            TestUserId,
            newTenantId,
            Arg.Is<DateTimeOffset?>(dt => dt.HasValue));
    }

    [Fact]
    public async Task SetTenant_Success_WritesCookie()
    {
        // Arrange — Keycloak-token principal (has sub, no tid)
        var externalSub = "keycloak|user-789";
        var claims = new[]
        {
            new Claim("sub", externalSub)
        };
        SetAuthenticatedExternalIdpUser(claims);

        var user = new UserDto(TestUserId, externalSub, "user@example.com", "Test User", true);
        _userRepository.GetByExternalUserIdAsync(externalSub, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));

        var response = new SetTenantResponseDto(false, null, TestToken);
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(response));

        var request = new SetTenantRequestDto(TestTenantId);

        // Act
        await _sut.SetTenant(request);

        // Assert
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestToken);
    }

    #endregion

    #region POST /auth/refresh

    [Fact]
    public async Task Refresh_Unauthenticated_Returns401()
    {
        // Act
        var result = await _sut.Refresh();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Refresh_Success_Returns200AndRewritesCookie()
    {
        // Arrange
        var authTimeUnix = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString()),
            new Claim("auth_time", authTimeUnix.ToString())
        };
        SetAuthenticatedUser(claims);

        var newToken = "refreshed-jwt-token-xyz";
        _authSessionService.RefreshTokenAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Ok(newToken));

        // Act
        var result = await _sut.Refresh();

        // Assert
        result.Should().BeOfType<OkResult>();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), newToken);
    }

    [Fact]
    public async Task Refresh_MembershipRevoked_Returns403AndClearsCookie()
    {
        // Arrange
        var authTimeUnix = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString()),
            new Claim("auth_time", authTimeUnix.ToString())
        };
        SetAuthenticatedUser(claims);

        _authSessionService.RefreshTokenAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Fail("Membership has been revoked", 403));

        // Act
        var result = await _sut.Refresh();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        _authCookieWriter.Received(1).ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task Refresh_SessionPastAbsoluteLifetime_Returns401WithReauthRequiredAndClearsCookie()
    {
        // Arrange
        var authTimeUnix = DateTimeOffset.UtcNow.AddMinutes(-600).ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString()),
            new Claim("auth_time", authTimeUnix.ToString())
        };
        SetAuthenticatedUser(claims);

        _authSessionService.RefreshTokenAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset>())
            .Returns(OperationResult<string>.Fail("Session has reached its maximum lifetime.", 401, GroundUp.Core.ErrorCodes.ReauthRequired));

        // Act
        var result = await _sut.Refresh();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be(GroundUp.Core.ErrorCodes.ReauthRequired);
        await _authCookieWriter.Received(1).ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task SetTenant_SessionPastAbsoluteLifetime_Returns401WithReauthRequired()
    {
        // Arrange
        var authTimeUnix = DateTimeOffset.UtcNow.AddMinutes(-600).ToUnixTimeSeconds();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("auth_time", authTimeUnix.ToString())
        });
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Fail("Session has reached its maximum lifetime.", 401, GroundUp.Core.ErrorCodes.ReauthRequired));

        // Act
        var result = await _sut.SetTenant(new SetTenantRequestDto(TestTenantId));

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be(GroundUp.Core.ErrorCodes.ReauthRequired);
    }

    [Fact]
    public async Task Refresh_MissingAuthTimeClaim_Returns400()
    {
        // Arrange — authenticated but missing auth_time claim
        var claims = new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString())
        };
        SetAuthenticatedUser(claims);

        // Act
        var result = await _sut.Refresh();

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region POST /auth/logout

    [Fact]
    public async Task Logout_ClearsCookieAndReturns200WithLogoutUrl()
    {
        // Act
        var result = await _sut.Logout();

        // Assert — cookie cleared
        _authCookieWriter.Received(1).ClearAuthCookieAsync(Arg.Any<HttpContext>());

        // Assert — 200 with logoutUrl pointing to Keycloak end_session
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        var logoutUrl = body["logoutUrl"]?.ToString();
        logoutUrl.Should().NotBeNullOrEmpty();
        logoutUrl.Should().Contain("/protocol/openid-connect/logout");
        logoutUrl.Should().Contain("client_id=groundup-app");
        logoutUrl.Should().Contain("post_logout_redirect_uri=");
    }

    [Fact]
    public async Task Logout_LogoutUrlContainsRealmFromKeycloakOptions()
    {
        // Act
        var result = await _sut.Logout();

        // Assert — logoutUrl uses the configured realm
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        var logoutUrl = body["logoutUrl"]?.ToString();
        logoutUrl.Should().Contain(_keycloakOptions.SharedRealmName);
        logoutUrl.Should().Contain(_keycloakOptions.PublicBaseUrl);
    }

    #endregion

    #region GET /auth/me/tenants

    [Fact]
    public async Task GetMyTenants_Unauthenticated_Returns401()
    {
        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetMyTenants_AuthenticatedUserWithNoMemberships_ReturnsEmptyList()
    {
        // Arrange
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString())
        });

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>()));

        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value as Array;
        list.Should().NotBeNull();
        list!.Length.Should().Be(0);
    }

    [Fact]
    public async Task GetMyTenants_AuthenticatedUserWithOneStandardTenant_ReturnsCorrectShape()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", tenantId.ToString())
        });

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, tenantId, "ext-1", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var tenants = new List<TenantDto>
        {
            new(tenantId, "Acme Corp", "acme-corp", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true)
        };
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(tenants));

        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value as IList<object>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);

        var item = GetAnonymousObjectProperties(list[0]);
        item["id"].Should().Be(tenantId);
        item["name"].Should().Be("Acme Corp");
        item["slug"].Should().Be("acme-corp");
        item["type"].Should().Be("standard");
        item["switchable"].Should().Be(true);
        item["current"].Should().Be(true);
    }

    [Fact]
    public async Task GetMyTenants_AuthenticatedUserWithMultipleTenants_ReturnsCorrectListWithCurrentFlag()
    {
        // Arrange
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", tenantId1.ToString())
        });

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, tenantId1, "ext-1", true),
            new(Guid.NewGuid(), TestUserId, tenantId2, "ext-2", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var tenants = new List<TenantDto>
        {
            new(tenantId1, "Tenant One", "tenant-one", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true),
            new(tenantId2, "Tenant Two", "tenant-two", TenantType.Standard, OnboardingMode.InviteOnly, null, null, true)
        };
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(tenants));

        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value as IList<object>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(2);

        var first = GetAnonymousObjectProperties(list[0]);
        first["id"].Should().Be(tenantId1);
        first["current"].Should().Be(true);

        var second = GetAnonymousObjectProperties(list[1]);
        second["id"].Should().Be(tenantId2);
        second["current"].Should().Be(false);
    }

    [Fact]
    public async Task GetMyTenants_EnterpriseTenant_MarkedAsSwitchableFalse()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString())
        });

        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, tenantId, "ext-1", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        // Enterprise tenant has a non-null RealmName
        var tenants = new List<TenantDto>
        {
            new(tenantId, "Enterprise Co", "enterprise-co", TenantType.Enterprise, OnboardingMode.InviteOnly, null, "enterprise-realm", true)
        };
        _tenantRepository.GetByIdsBypassFilterAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<TenantDto>>.Ok(tenants));

        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value as IList<object>;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);

        var item = GetAnonymousObjectProperties(list[0]);
        item["id"].Should().Be(tenantId);
        item["name"].Should().Be("Enterprise Co");
        item["slug"].Should().Be("enterprise-co");
        item["type"].Should().Be("enterprise");
        item["switchable"].Should().Be(false);
        item["subdomain"].Should().Be("enterprise-co");
    }

    #endregion

    #region POST /auth/organizations

    private const string TestExternalUserId = "keycloak-sub-org-creator";

    [Fact]
    public async Task CreateOrganization_Unauthenticated_Returns401()
    {
        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_IdentityProviderPrincipal_Returns401()
    {
        // Arrange — a raw identity-provider token has no GroundUp user id, even if its sub looks like a GUID
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", TestUserId.ToString()) });

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_SubIsNotAGuid_Returns401()
    {
        // Arrange
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", "not-a-guid"),
            new Claim("tid", TestTenantId.ToString())
        });

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_UserNotFound_Returns401()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        _userRepository.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound());

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_UserInactive_Returns401()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        _userRepository.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(
                new UserDto(TestUserId, TestExternalUserId, "creator@example.com", "Creator", IsActive: false)));

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotProvisionedAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateOrganization_MissingOrBlankName_Returns400(string? organizationName)
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto(organizationName));

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_NullBody_Returns400()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();

        // Act
        var result = await _sut.CreateOrganization(null);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_NameLongerThan200Characters_Returns400()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto(new string('a', 201)));

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        await AssertNotProvisionedAsync();
    }

    [Fact]
    public async Task CreateOrganization_NameExactly200Characters_IsAccepted()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();
        SetupSuccessfulProvisioning();

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto(new string('a', 200)));

        // Assert
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task CreateOrganization_ValidRequest_Returns201WithIdNameAndSlug()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();
        var newTenantId = SetupSuccessfulProvisioning();

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(201);
        var body = GetAnonymousObjectProperties(objectResult.Value!);
        body["id"].Should().Be(newTenantId);
        body["name"].Should().Be("Acme Corp");
        body["slug"].Should().Be("acme-corp");
    }

    [Fact]
    public async Task CreateOrganization_ValidRequest_ProvisionsForResolvedUserWithTrimmedName()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();
        SetupSuccessfulProvisioning();

        // Act
        await _sut.CreateOrganization(new CreateOrganizationRequestDto("  Acme Corp  "));

        // Assert
        await _organizationProvisioningService.Received(1).ProvisionAsync(
            Arg.Is<OrganizationProvisioningRequest>(r =>
                r.UserId == TestUserId &&
                r.ExternalUserId == TestExternalUserId &&
                r.OrganizationName == "Acme Corp"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrganization_ValidRequest_DoesNotChangeTheSessionToken()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();
        SetupSuccessfulProvisioning();

        // Act
        await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        // Assert — the active tenant stays as is; the user switches later via set-tenant
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        await _authSessionService.DidNotReceive().SetTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>());
    }

    [Fact]
    public async Task CreateOrganization_SlugCollision_Returns409WithErrorCode()
    {
        // Arrange
        SetAuthenticatedOrgCreator();
        SetupOrgCreator();
        _organizationProvisioningService.ProvisionAsync(
                Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<OrganizationProvisioningResult>.Fail(
                "Unable to create organization — slug unavailable after retries.", 409, "SLUG_COLLISION"));

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be("SLUG_COLLISION");
    }

    private void SetAuthenticatedOrgCreator()
    {
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString())
        });
    }

    private void SetupOrgCreator()
    {
        _userRepository.GetByIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(
                new UserDto(TestUserId, TestExternalUserId, "creator@example.com", "Creator", IsActive: true)));
    }

    private Guid SetupSuccessfulProvisioning()
    {
        var newTenantId = Guid.NewGuid();
        _organizationProvisioningService.ProvisionAsync(
                Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.ArgAt<OrganizationProvisioningRequest>(0);
                return OperationResult<OrganizationProvisioningResult>.Ok(
                    new OrganizationProvisioningResult(newTenantId, request.OrganizationName, "acme-corp"));
            });
        return newTenantId;
    }

    private async Task AssertNotProvisionedAsync()
    {
        await _organizationProvisioningService.DidNotReceive().ProvisionAsync(
            Arg.Any<OrganizationProvisioningRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region POST /auth/tenants/{id}/leave

    [Fact]
    public async Task LeaveTenant_Unauthenticated_Returns401()
    {
        // Act
        var result = await _sut.LeaveTenant(TestTenantId);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task LeaveTenant_UserNotMemberOfTenant_Returns404()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", TestTenantId.ToString())
        });

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>()));

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(notFoundResult.Value!);
        body["error"].Should().NotBeNull();
    }

    [Fact]
    public async Task LeaveTenant_UserIsSoleAdmin_Returns403()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", targetTenantId.ToString())
        });

        var memberships = new List<UserTenantDto>
        {
            new(membershipId, TestUserId, targetTenantId, "ext-1", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        // LastAdminGuard: only 1 admin exists — block
        var adminHolders = new List<UserRoleDto>
        {
            new(Guid.NewGuid(), TestUserId, Guid.NewGuid(), targetTenantId, "TenantAdmin")
        };
        _userRoleRepository.GetTenantAdminHoldersAsync(targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(adminHolders));

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task LeaveTenant_SuccessfulLeave_DeactivatesMembershipAndReturnsSuccess()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", otherTenantId.ToString()) // Viewing a different tenant
        });

        var memberships = new List<UserTenantDto>
        {
            new(membershipId, TestUserId, targetTenantId, "ext-1", true),
            new(Guid.NewGuid(), TestUserId, otherTenantId, "ext-2", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        // LastAdminGuard: 2 admins exist — allow
        var adminHolders = new List<UserRoleDto>
        {
            new(Guid.NewGuid(), TestUserId, Guid.NewGuid(), targetTenantId, "TenantAdmin"),
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), targetTenantId, "TenantAdmin")
        };
        _userRoleRepository.GetTenantAdminHoldersAsync(targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(adminHolders));

        _userTenantRepository.DeactivateMembershipAsync(TestUserId, targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["success"].Should().Be(true);

        // Verify membership was deactivated
        await _userTenantRepository.Received(1).DeactivateMembershipAsync(
            TestUserId, targetTenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveTenant_DeactivationFails_ReturnsErrorInsteadOfSuccess()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", Guid.NewGuid().ToString())
        });

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(new List<UserTenantDto>
            {
                new(Guid.NewGuid(), TestUserId, targetTenantId, "ext-1", true)
            }));
        _userRoleRepository.GetTenantAdminHoldersAsync(targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>()));
        _userTenantRepository.DeactivateMembershipAsync(TestUserId, targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("A conflict occurred while leaving the tenant.", 409));

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task LeaveTenant_RegularMemberOfTenantWithOneAdmin_DeactivatesMembershipAndReturnsSuccess()
    {
        // Arrange — the leaving user is not an admin; the tenant has exactly one other admin
        var targetTenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", otherTenantId.ToString())
        });

        var memberships = new List<UserTenantDto>
        {
            new(membershipId, TestUserId, targetTenantId, "ext-1", true),
            new(Guid.NewGuid(), TestUserId, otherTenantId, "ext-2", true)
        };
        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        var adminHolders = new List<UserRoleDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), targetTenantId, "TenantAdmin")
        };
        _userRoleRepository.GetTenantAdminHoldersAsync(targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(adminHolders));

        _userTenantRepository.DeactivateMembershipAsync(TestUserId, targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        await _userTenantRepository.Received(1).DeactivateMembershipAsync(
            TestUserId, targetTenantId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LeaveTenant_LeavingCurrentTenant_BecomesTenantlessAndNeverSwitchesToAnotherTenant(int otherMemberships)
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        var authTime = DateTimeOffset.UtcNow.AddHours(-1);
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", targetTenantId.ToString()), // Currently working in the tenant being left
            new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString())
        });
        SetupLeavableMemberships(targetTenantId, otherMemberships);
        _authSessionService.IssueTenantlessSessionAsync(TestUserId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<string>.Ok("tenantless-token"));

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert — tenantless token written, auth_time preserved, no other tenant selected
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["success"].Should().Be(true);
        body["currentTenantId"].Should().BeNull();
        body.Should().NotContainKey("noTenants");

        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), "tenantless-token");
        await _authSessionService.Received(1).IssueTenantlessSessionAsync(
            TestUserId,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && d.Value.ToUnixTimeSeconds() == authTime.ToUnixTimeSeconds()));
        await _authSessionService.DidNotReceive().SetTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>());
    }

    [Fact]
    public async Task LeaveTenant_LeavingCurrentTenant_TenantlessTokenFails_ClearsCookieSoOldTokenIsNotKept()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", targetTenantId.ToString())
        });
        SetupLeavableMemberships(targetTenantId, otherMemberships: 1);
        _authSessionService.IssueTenantlessSessionAsync(TestUserId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<string>.NotFound("User not found"));

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        GetAnonymousObjectProperties(okResult.Value!)["currentTenantId"].Should().BeNull();
        _authCookieWriter.Received(1).ClearAuthCookieAsync(Arg.Any<HttpContext>());
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LeaveTenant_LeavingDifferentTenant_SessionUnchangedAndCurrentTenantReported()
    {
        // Arrange
        var targetTenantId = Guid.NewGuid();
        var currentTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("tid", currentTenantId.ToString())
        });
        SetupLeavableMemberships(targetTenantId, otherMemberships: 0);

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["success"].Should().Be(true);
        body["currentTenantId"].Should().Be(currentTenantId);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        _authCookieWriter.DidNotReceive().ClearAuthCookieAsync(Arg.Any<HttpContext>());
        await _authSessionService.DidNotReceive().IssueTenantlessSessionAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>());
    }

    [Fact]
    public async Task LeaveTenant_FromTenantlessSession_SessionUnchangedAndNoCurrentTenantReported()
    {
        // Arrange — tenantless GroundUp token (no tid)
        var targetTenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        SetupLeavableMemberships(targetTenantId, otherMemberships: 1);

        // Act
        var result = await _sut.LeaveTenant(targetTenantId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["success"].Should().Be(true);
        body["currentTenantId"].Should().BeNull();
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        _authCookieWriter.DidNotReceive().ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task LeaveTenant_IdentityProviderPrincipal_Returns401()
    {
        // Arrange — the sub of an identity-provider token is not a GroundUp user id, even when it parses as a GUID
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", TestUserId.ToString()) });

        // Act
        var result = await _sut.LeaveTenant(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        await _userTenantRepository.DidNotReceive().DeactivateMembershipAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The user holds an active membership in <paramref name="targetTenantId"/> plus
    /// <paramref name="otherMemberships"/> others, and may leave (a second admin exists).
    /// </summary>
    private void SetupLeavableMemberships(Guid targetTenantId, int otherMemberships)
    {
        var memberships = new List<UserTenantDto>
        {
            new(Guid.NewGuid(), TestUserId, targetTenantId, "ext-target", true)
        };
        for (var i = 0; i < otherMemberships; i++)
        {
            memberships.Add(new(Guid.NewGuid(), TestUserId, Guid.NewGuid(), $"ext-other-{i}", true));
        }

        _userTenantRepository.GetAllMembershipsForUserAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok(memberships));

        // LastAdminGuard: two admins, so leaving is allowed
        _userRoleRepository.GetTenantAdminHoldersAsync(targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(new List<UserRoleDto>
            {
                new(Guid.NewGuid(), TestUserId, Guid.NewGuid(), targetTenantId, "TenantAdmin"),
                new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), targetTenantId, "TenantAdmin")
            }));

        _userTenantRepository.DeactivateMembershipAsync(TestUserId, targetTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
    }

    #endregion

    #region Tenantless sessions

    [Fact]
    public async Task Callback_NoCurrentTenant_Returns200WithEmptyTenantListAndNoSelection()
    {
        // Arrange
        _authFlowService.HandleCallbackAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(FlowResult.NoCurrentTenant()));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["requiresTenantSelection"].Should().Be(false);
        ((IEnumerable<TenantListItemDto>)body["tenants"]!).Should().BeEmpty();
        body.Should().NotContainKey("token");
    }

    [Fact]
    public async Task SetTenant_FromTenantlessGroundUpSession_UsesOwnUserIdAndPreservesAuthTime()
    {
        // Arrange — GroundUp token without tid: sub is the GroundUp user id (a GUID), not an external id
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-20);
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString())
        });
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(new SetTenantResponseDto(false, null, TestToken)));

        // Act
        var result = await _sut.SetTenant(new SetTenantRequestDto(TestTenantId));

        // Assert
        result.Should().BeOfType<OkResult>();
        await _userRepository.DidNotReceive().GetByExternalUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _authSessionService.Received(1).SetTenantAsync(
            TestUserId,
            TestTenantId,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && d.Value.ToUnixTimeSeconds() == authTime.ToUnixTimeSeconds()));
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestToken);
    }

    [Fact]
    public async Task SetTenant_SessionPastAbsoluteLifetime_Returns401AndWritesNoCookie()
    {
        // Arrange
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("auth_time", DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds().ToString())
        });
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Unauthorized("Session has reached its maximum lifetime."));

        // Act
        var result = await _sut.SetTenant(new SetTenantRequestDto(TestTenantId));

        // Assert
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(401);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CreateOrganization_FromTenantlessGroundUpSession_Returns201AndKeepsSessionTenantless()
    {
        // Arrange — GroundUp token with no tid
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        SetupOrgCreator();
        var newTenantId = SetupSuccessfulProvisioning();

        // Act
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(201);
        GetAnonymousObjectProperties(objectResult.Value!)["id"].Should().Be(newTenantId);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task GetMyTenants_IdentityProviderPrincipal_Returns401()
    {
        // Arrange
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", TestUserId.ToString()) });

        // Act
        var result = await _sut.GetMyTenants();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    #endregion

    #region Helper Methods

    private AuthController CreateController()
    {
        var controller = new AuthController(
            _authFlowService,
            _authSessionService,
            _authCookieWriter,
            _userRepository,
            _userTenantRepository,
            _tenantRepository,
            _lastAdminGuard,
            _organizationProvisioningService,
            Options.Create(_authOptions),
            Options.Create(_keycloakOptions),
            _antiforgery);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private void SetAuthenticatedUser(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        _sut.ControllerContext.HttpContext.User = principal;
    }

    /// <summary>
    /// Authenticates as a principal built from an identity-provider token (for example a Keycloak
    /// Bearer token): its sub is the provider's user id, not a GroundUp user id.
    /// </summary>
    private void SetAuthenticatedExternalIdpUser(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, AuthConstants.ExternalIdpAuthenticationType);
        _sut.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
    }

    private static Dictionary<string, object?> GetAnonymousObjectProperties(object obj)
    {
        var type = obj.GetType();
        var properties = type.GetProperties();
        var dict = new Dictionary<string, object?>();
        foreach (var prop in properties)
        {
            dict[prop.Name] = prop.GetValue(obj);
        }
        return dict;
    }

    #endregion
}
