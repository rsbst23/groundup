using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
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
    private readonly AuthOptions _authOptions;
    private readonly KeycloakOptions _keycloakOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAntiforgery _antiforgery;
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

        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _antiforgery = Substitute.For<IAntiforgery>();

        _sut = CreateController();
    }

    #region GET /auth/login

    [Fact]
    public async Task Login_FlowServiceReturnsSuccess_Returns302Redirect()
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
        var redirectResult = result.Should().BeOfType<RedirectResult>().Subject;
        redirectResult.Url.Should().Be(TestRedirectUrl);
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

    #region GET /auth/register

    [Fact]
    public async Task Register_WithoutOrganizationName_Returns400()
    {
        // Act
        var result = await _sut.Register(organizationName: null);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Register_WithEmptyOrganizationName_Returns400()
    {
        // Act
        var result = await _sut.Register(organizationName: "   ");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Register_WithOrganizationNameExceeding200Chars_Returns400()
    {
        // Arrange
        var longName = new string('A', 201);

        // Act
        var result = await _sut.Register(organizationName: longName);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Register_WithValidOrganizationName_Returns302Redirect()
    {
        // Arrange
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        // Act
        var result = await _sut.Register(organizationName: "Acme Corp");

        // Assert
        var redirectResult = result.Should().BeOfType<RedirectResult>().Subject;
        redirectResult.Url.Should().Be(TestRedirectUrl);
    }

    [Fact]
    public async Task Register_WithValidOrganizationName_PassesNewOrganizationFlowType()
    {
        // Arrange
        var initiationResult = new FlowInitiationResult(TestRedirectUrl, TestFlowStateId);
        _authFlowService.InitiateFlowAsync(
                Arg.Any<FlowInitiationRequest>(),
                Arg.Any<HttpContext>(),
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(initiationResult));

        // Act
        await _sut.Register(organizationName: "Acme Corp");

        // Assert
        await _authFlowService.Received(1).InitiateFlowAsync(
            Arg.Is<FlowInitiationRequest>(r =>
                r.FlowType == GroundUp.Auth.Core.Enums.FlowType.NewOrganization &&
                r.OrganizationName == "Acme Corp"),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
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
    public async Task Callback_SuccessfulFlow_Returns302Redirect()
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
        var redirectResult = result.Should().BeOfType<RedirectResult>().Subject;
        redirectResult.Url.Should().Be("/dashboard");
    }

    [Fact]
    public async Task Callback_SuccessfulFlowWithNullRedirectUrl_RedirectsToRoot()
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
        var redirectResult = result.Should().BeOfType<RedirectResult>().Subject;
        redirectResult.Url.Should().Be("/");
    }

    [Fact]
    public async Task Callback_TenantSelectionRequired_Returns200WithTenantList()
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
        SetAuthenticatedUser(claims);

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
        SetAuthenticatedUser(claims);

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
        SetAuthenticatedUser(claims);

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
        _authCookieWriter.Received(1).WriteAuthCookie(Arg.Any<HttpContext>(), TestToken);
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
        _authCookieWriter.Received(1).WriteAuthCookie(Arg.Any<HttpContext>(), newToken);
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
        _authCookieWriter.Received(1).ClearAuthCookie(Arg.Any<HttpContext>());
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
    public async Task Logout_ClearsCookieAndReturns200()
    {
        // Arrange — provide a client that does nothing
        var httpClient = new HttpClient(new FakeHttpMessageHandler());
        _httpClientFactory.CreateClient("KeycloakIdp").Returns(httpClient);

        // Act
        var result = await _sut.Logout();

        // Assert
        result.Should().BeOfType<OkResult>();
        _authCookieWriter.Received(1).ClearAuthCookie(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task Logout_KeycloakEndSessionFails_StillReturns200()
    {
        // Arrange — HttpClient throws to simulate Keycloak being down
        var httpClient = new HttpClient(new ThrowingHttpMessageHandler());
        _httpClientFactory.CreateClient("KeycloakIdp").Returns(httpClient);

        // Act
        var result = await _sut.Logout();

        // Assert — cookie cleared, 200 returned (best-effort Keycloak logout)
        result.Should().BeOfType<OkResult>();
        _authCookieWriter.Received(1).ClearAuthCookie(Arg.Any<HttpContext>());
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
            Options.Create(_authOptions),
            Options.Create(_keycloakOptions),
            _httpClientFactory,
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

    /// <summary>
    /// A fake HttpMessageHandler that returns a successful empty response.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// A fake HttpMessageHandler that throws to simulate network failure.
    /// </summary>
    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network failure");
        }
    }

    #endregion
}
