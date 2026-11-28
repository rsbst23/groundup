using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Api;
using GroundUp.Auth.Api.Controllers;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Membership;
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
    private readonly IMembershipService _membershipService = Substitute.For<IMembershipService>();
    private readonly IPermissionService _permissionService = Substitute.For<IPermissionService>();
    private readonly AuthOptions _authOptions;
    private readonly KeycloakOptions _keycloakOptions;
    private readonly IAntiforgery _antiforgery;
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
        _permissionService.GetPermissionSummaryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionSummaryDto(false, []));
        _authFlowService = Substitute.For<IAuthFlowService>();
        _authSessionService = Substitute.For<IAuthSessionService>();
        _authCookieWriter = Substitute.For<IAuthCookieWriter>();

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

    [Fact]
    public async Task Login_WithReturnUrl_PassesItToTheFlowService()
    {
        // Arrange
        _authFlowService.InitiateFlowAsync(Arg.Any<FlowInitiationRequest>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(new FlowInitiationResult(TestRedirectUrl, TestFlowStateId)));

        // Act
        await _sut.Login(forceLogin: true, returnUrl: "/session");

        // Assert
        await _authFlowService.Received(1).InitiateFlowAsync(
            Arg.Is<FlowInitiationRequest>(r => r.ReturnUrl == "/session" && r.ForceLogin && r.FlowType == FlowType.Login),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_InvalidReturnUrl_ReturnsTheStatusAndErrorCode()
    {
        // Arrange
        _authFlowService.InitiateFlowAsync(Arg.Any<FlowInitiationRequest>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Fail(
                "The return URL must be a path on this site.", 400, AuthErrorCodes.InvalidReturnUrl));

        // Act
        var result = await _sut.Login(returnUrl: "https://evil.example.com");

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(400);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be(AuthErrorCodes.InvalidReturnUrl);
    }

    #endregion

    #region POST /auth/signup

    [Theory]
    [InlineData(null, FlowType.SimpleSignup)]
    [InlineData("Acme", FlowType.NewOrganization)]
    public async Task Signup_WithReturnUrl_PassesItToTheFlowService(string? organizationName, FlowType expectedFlowType)
    {
        // Arrange
        _authFlowService.InitiateFlowAsync(Arg.Any<FlowInitiationRequest>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowInitiationResult>.Ok(new FlowInitiationResult(TestRedirectUrl, TestFlowStateId)));

        // Act
        await _sut.Signup(new SignupRequestDto(organizationName), returnUrl: "/session");

        // Assert
        await _authFlowService.Received(1).InitiateFlowAsync(
            Arg.Is<FlowInitiationRequest>(r => r.ReturnUrl == "/session" && r.FlowType == expectedFlowType),
            Arg.Any<HttpContext>(),
            Arg.Any<CancellationToken>());
    }

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

    [Fact]
    public async Task Callback_WithReturnUrl_RedirectsWithTheOutcomeAndNoToken()
    {
        // Arrange
        _authFlowService.HandleCallbackAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(FlowResult.Success(TestToken) with { ReturnUrl = "/session" }));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("/session?result=signed_in");
        redirect.Permanent.Should().BeFalse();
        redirect.Url.Should().NotContain(TestToken);
    }

    [Fact]
    public async Task Callback_ErrorWithReturnUrl_RedirectsWithTheCode()
    {
        // Arrange
        _authFlowService.HandleCallbackAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(
                FlowResult.Error("REAUTH_REQUIRED", "Sign in again.", 401) with { ReturnUrl = "/session" }));

        // Act
        var result = await _sut.Callback(code: "auth-code", state: "state-token");

        // Assert
        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/session?error=REAUTH_REQUIRED");
    }

    [Fact]
    public async Task Callback_ProviderError_GoesToHandleProviderErrorAsync()
    {
        // Arrange
        _authFlowService.HandleProviderErrorAsync("state-token", "access_denied", "cancelled", Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(
                FlowResult.Error(AuthErrorCodes.IdpError, "The identity provider reported an error.") with { ReturnUrl = "/session", IdpError = "access_denied" }));

        // Act
        var result = await _sut.Callback(code: null, state: "state-token", error: "access_denied", errorDescription: "cancelled");

        // Assert
        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/session?error=IDP_ERROR&idpError=access_denied");
        await _authFlowService.DidNotReceiveWithAnyArgs().HandleCallbackAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Callback_ProviderErrorWithoutReturnUrl_Returns400JsonWithIdpError()
    {
        // Arrange
        _authFlowService.HandleProviderErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<FlowResult>.Ok(FlowResult.Error(AuthErrorCodes.IdpError, "The identity provider reported an error.")));

        // Act
        var result = await _sut.Callback(code: null, state: "state-token", error: "access_denied");

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(400);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be(AuthErrorCodes.IdpError);
    }

    [Fact]
    public async Task Callback_ProviderErrorWithoutState_Returns400()
    {
        // Act
        var result = await _sut.Callback(code: null, state: null, error: "access_denied");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        await _authFlowService.DidNotReceiveWithAnyArgs().HandleProviderErrorAsync(default!, default!, default, default!, default);
    }

    #endregion

    #region GET /auth/csrf

    [Fact]
    public void Csrf_Unauthenticated_Returns401AndIssuesNoToken()
    {
        // Act
        var result = _sut.Csrf();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        _antiforgery.DidNotReceive().GetAndStoreTokens(Arg.Any<HttpContext>());
    }

    [Fact]
    public void Csrf_Authenticated_ReturnsRequestTokenWithNoStore()
    {
        // Arrange
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        _antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>())
            .Returns(new AntiforgeryTokenSet("request-token-123", "cookie-token", "CsrfToken", "X-CSRF-Token"));

        // Act
        var result = _sut.Csrf();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        GetAnonymousObjectProperties(ok.Value!)["token"].Should().Be("request-token-123");
        _sut.ControllerContext.HttpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Me_Authenticated_DoesNotIssueAntiforgeryTokens()
    {
        // Arrange — tokens come only from GET /auth/csrf now
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });

        // Act
        await _sut.Me();

        // Assert
        _antiforgery.DidNotReceive().GetAndStoreTokens(Arg.Any<HttpContext>());
    }

    #endregion

    #region GET /auth/me

    [Fact]
    public async Task Me_Unauthenticated_Returns401()
    {
        // Arrange — controller has DefaultHttpContext with no authenticated user (default)

        // Act
        var result = (await _sut.Me());

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Me_PendingSelectionPrincipalNoTidClaim_ReturnsIdentityWithNullTenant()
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
        var result = (await _sut.Me());

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(okResult.Value!);
        body["userId"].Should().Be("keycloak-sub-12345");
        body["email"].Should().Be("user@example.com");
        body["displayName"].Should().Be("Test User");
        body["tenantId"].Should().BeNull();
    }

    [Fact]
    public async Task Me_AuthenticatedGroundUpToken_ReturnsFullClaims()
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
        var result = (await _sut.Me());

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

    [Fact]
    public async Task Me_TokenTimeClaims_ReturnsIssuedAtExpiresAtAndAuthTimeInUtc()
    {
        // Arrange
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("iat", "1795800000"),
            new Claim("exp", "1795800900"),
            new Claim("auth_time", "1795799000")
        });

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["issuedAt"].Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795800000));
        body["expiresAt"].Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795800900));
        body["authTime"].Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795799000));
        ((DateTimeOffset)body["expiresAt"]!).Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Me_NoAuthTimeClaim_ReturnsNullAuthTime()
    {
        // Arrange — a Keycloak principal on the tenant picker has iat and exp but may lack auth_time
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", "keycloak-sub-12345"),
            new Claim("iat", "1795800000"),
            new Claim("exp", "1795800300")
        });

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["authTime"].Should().BeNull();
        body["expiresAt"].Should().Be(DateTimeOffset.FromUnixTimeSeconds(1795800300));
    }

    [Fact]
    public async Task Me_UnparseableTimeClaim_ReturnsNull()
    {
        // Arrange
        SetAuthenticatedUser(new[]
        {
            new Claim("sub", TestUserId.ToString()),
            new Claim("exp", "not-a-number")
        });

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["expiresAt"].Should().BeNull();
        body["issuedAt"].Should().BeNull();
    }

    [Fact]
    public async Task Me_GroundUpUser_ReturnsTheirPermissionSummary()
    {
        // Arrange
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", TestTenantId.ToString()) });
        _permissionService.GetPermissionSummaryAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new PermissionSummaryDto(false, ["auth.members.read", "docs.read"]));

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["allPermissions"].Should().Be(false);
        body["permissions"].Should().BeEquivalentTo(new List<string> { "auth.members.read", "docs.read" });
    }

    [Fact]
    public async Task Me_Admin_ReturnsAllPermissions()
    {
        // Arrange
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", TestTenantId.ToString()) });
        _permissionService.GetPermissionSummaryAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(new PermissionSummaryDto(true, []));

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["allPermissions"].Should().Be(true);
    }

    [Fact]
    public async Task Me_IdentityProviderSub_NoPermissionsWithoutALookup()
    {
        // Arrange: a pending-selection token carries the identity provider's sub, not a GroundUp user id
        SetAuthenticatedUser(new[] { new Claim("sub", "keycloak-sub-12345") });

        // Act
        var body = GetAnonymousObjectProperties(((OkObjectResult)(await _sut.Me())).Value!);

        // Assert
        body["allPermissions"].Should().Be(false);
        (body["permissions"] as List<string>).Should().BeEmpty();
        await _permissionService.DidNotReceiveWithAnyArgs().GetPermissionSummaryAsync(default, default);
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

        var response = new SetTenantResponseDto(false, null, TestToken);
        _authSessionService.SetTenantForExternalUserAsync(externalSub, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(response));

        var request = new SetTenantRequestDto(TestTenantId);

        // Act
        var result = await _sut.SetTenant(request);

        // Assert — the service resolves the user by external id; the controller does not
        await _authSessionService.Received(1).SetTenantForExternalUserAsync(externalSub, TestTenantId, Arg.Any<CancellationToken>());
        await _authSessionService.DidNotReceive().SetTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>());
        result.Should().BeOfType<OkResult>();
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), TestToken);
    }

    [Fact]
    public async Task SetTenant_KeycloakPrincipalWithoutGroundUpUser_Returns404WithError()
    {
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", "keycloak|unknown") });
        _authSessionService.SetTenantForExternalUserAsync("keycloak|unknown", TestTenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<SetTenantResponseDto>.Fail("User not found.", 404, GroundUp.Core.ErrorCodes.UserNotFound));

        var result = await _sut.SetTenant(new SetTenantRequestDto(TestTenantId));

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = GetAnonymousObjectProperties(notFound.Value!);
        body.Keys.Should().BeEquivalentTo("error");
        body["error"].Should().Be("User not found.");
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

        _authSessionService.SetTenantForExternalUserAsync(externalSub, TestTenantId, Arg.Any<CancellationToken>())
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
    public async Task SetTenant_GroundUpToken_Success_WritesCookie()
    {
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        _authSessionService.SetTenantAsync(TestUserId, TestTenantId, Arg.Any<DateTimeOffset?>())
            .Returns(OperationResult<SetTenantResponseDto>.Ok(new SetTenantResponseDto(false, null, TestToken)));

        await _sut.SetTenant(new SetTenantRequestDto(TestTenantId));

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
    public async Task Logout_ExpiresTheAntiforgeryCookie()
    {
        // Act
        await _sut.Logout();

        // Assert
        var setCookie = _sut.ControllerContext.HttpContext.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("CsrfToken=;");
        setCookie.Should().Contain("expires=Thu, 01 Jan 1970");
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

    // The list rules (active memberships, standard vs enterprise, current tenant) are tested in
    // MembershipServiceTests. These tests cover the HTTP adapter and the unchanged JSON shapes.

    [Fact]
    public async Task GetMyTenants_Unauthenticated_Returns401()
    {
        var result = await _sut.GetMyTenants();

        result.Should().BeOfType<UnauthorizedResult>();
        await _membershipService.DidNotReceiveWithAnyArgs().GetMyTenantsAsync(default);
    }

    [Fact]
    public async Task GetMyTenants_ServiceReturnsEmpty_ReturnsEmptyList()
    {
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", TestTenantId.ToString()) });
        GivenMyTenants();

        var result = await _sut.GetMyTenants();

        var list = result.Should().BeOfType<OkObjectResult>().Subject.Value as IList<object>;
        list.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetMyTenants_ServiceUnauthorized_Returns401()
    {
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        _membershipService.GetMyTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<MyTenantDto>>.Unauthorized());

        var result = await _sut.GetMyTenants();

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetMyTenants_StandardTenant_HasUnchangedShape()
    {
        var tenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", tenantId.ToString()) });
        GivenMyTenants(new MyTenantDto(tenantId, "Acme Corp", "acme-corp", "standard", true, true, null));

        var result = await _sut.GetMyTenants();

        var list = (IList<object>)result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var item = GetAnonymousObjectProperties(list.Single());
        item.Keys.Should().BeEquivalentTo("id", "name", "slug", "type", "switchable", "current");
        item["id"].Should().Be(tenantId);
        item["name"].Should().Be("Acme Corp");
        item["slug"].Should().Be("acme-corp");
        item["type"].Should().Be("standard");
        item["switchable"].Should().Be(true);
        item["current"].Should().Be(true);
    }

    [Fact]
    public async Task GetMyTenants_EnterpriseTenant_HasUnchangedShape()
    {
        var tenantId = Guid.NewGuid();
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", TestTenantId.ToString()) });
        GivenMyTenants(new MyTenantDto(tenantId, "Enterprise Co", "enterprise-co", "enterprise", false, null, "enterprise-co"));

        var result = await _sut.GetMyTenants();

        var list = (IList<object>)result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        var item = GetAnonymousObjectProperties(list.Single());
        item.Keys.Should().BeEquivalentTo("id", "name", "slug", "type", "switchable", "subdomain");
        item["type"].Should().Be("enterprise");
        item["switchable"].Should().Be(false);
        item["subdomain"].Should().Be("enterprise-co");
    }

    [Fact]
    public async Task GetMyTenants_KeepsServiceOrder()
    {
        SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()) });
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        GivenMyTenants(
            new MyTenantDto(first, "One", "one", "standard", true, false, null),
            new MyTenantDto(second, "Two", "two", "standard", true, false, null));

        var result = await _sut.GetMyTenants();

        var list = (IList<object>)result.Should().BeOfType<OkObjectResult>().Subject.Value!;
        list.Select(o => GetAnonymousObjectProperties(o)["id"]).Should().Equal(first, second);
    }

    private void GivenMyTenants(params MyTenantDto[] tenants)
        => _membershipService.GetMyTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(OperationResult<IReadOnlyList<MyTenantDto>>.Ok(tenants));

    #endregion

    #region POST /auth/organizations

    // Validation, the user check and provisioning are tested in OrganizationProvisioningServiceTests.
    // These tests cover the HTTP adapter.

    [Fact]
    public async Task CreateOrganization_Unauthenticated_Returns401()
    {
        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotCalledAsync();
    }

    [Fact]
    public async Task CreateOrganization_IdentityProviderPrincipal_Returns401()
    {
        // A raw identity-provider token has no GroundUp user id, even if its sub looks like a GUID
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", TestUserId.ToString()) });

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotCalledAsync();
    }

    [Fact]
    public async Task CreateOrganization_SubIsNotAGuid_Returns401()
    {
        SetAuthenticatedUser(new[] { new Claim("sub", "not-a-guid"), new Claim("tid", TestTenantId.ToString()) });

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        result.Should().BeOfType<UnauthorizedResult>();
        await AssertNotCalledAsync();
    }

    [Fact]
    public async Task CreateOrganization_PassesRequestToService()
    {
        SetAuthenticatedOrgCreator();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.Ok(new OrganizationProvisioningResult(Guid.NewGuid(), "Acme", "acme")));
        var request = new CreateOrganizationRequestDto("  Acme Corp  ");

        await _sut.CreateOrganization(request);

        await _organizationProvisioningService.Received(1).CreateForCurrentUserAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrganization_NullBody_PassesEmptyRequest()
    {
        SetAuthenticatedOrgCreator();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.BadRequest("Organization name is required."));

        var result = await _sut.CreateOrganization(null);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _organizationProvisioningService.Received(1).CreateForCurrentUserAsync(
            Arg.Is<CreateOrganizationRequestDto>(r => r.OrganizationName == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrganization_ServiceUnauthorized_Returns401()
    {
        SetAuthenticatedOrgCreator();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.Unauthorized());

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme"));

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task CreateOrganization_ServiceBadRequest_Returns400WithError()
    {
        SetAuthenticatedOrgCreator();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.BadRequest("Organization name must not exceed 200 characters."));

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto(new string('a', 201)));

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        GetAnonymousObjectProperties(badRequest.Value!)["error"].Should().Be("Organization name must not exceed 200 characters.");
    }

    [Fact]
    public async Task CreateOrganization_Success_Returns201WithIdNameAndSlugAndKeepsSession()
    {
        SetAuthenticatedOrgCreator();
        var newTenantId = Guid.NewGuid();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.Ok(new OrganizationProvisioningResult(newTenantId, "Acme Corp", "acme-corp")));

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(201);
        var body = GetAnonymousObjectProperties(objectResult.Value!);
        body["id"].Should().Be(newTenantId);
        body["name"].Should().Be("Acme Corp");
        body["slug"].Should().Be("acme-corp");

        // The active tenant stays as is; the user switches later via set-tenant
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        await _authSessionService.DidNotReceive().SetTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>());
    }

    [Fact]
    public async Task CreateOrganization_SlugCollision_Returns409WithErrorCode()
    {
        SetAuthenticatedOrgCreator();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.Fail(
            "Unable to create organization — slug unavailable after retries.", 409, "SLUG_COLLISION"));

        var result = await _sut.CreateOrganization(new CreateOrganizationRequestDto("Acme Corp"));

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
        GetAnonymousObjectProperties(objectResult.Value!)["errorCode"].Should().Be("SLUG_COLLISION");
    }

    private void SetAuthenticatedOrgCreator()
        => SetAuthenticatedUser(new[] { new Claim("sub", TestUserId.ToString()), new Claim("tid", TestTenantId.ToString()) });

    private void GivenCreateResult(OperationResult<OrganizationProvisioningResult> result)
        => _organizationProvisioningService.CreateForCurrentUserAsync(Arg.Any<CreateOrganizationRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private async Task AssertNotCalledAsync()
        => await _organizationProvisioningService.DidNotReceiveWithAnyArgs().CreateForCurrentUserAsync(default!, default);

    #endregion

    #region POST /auth/tenants/{id}/leave

    // The leave rules (membership, last-admin guard, revocation, tenantless session) are tested in
    // MembershipServiceTests. These tests cover only the HTTP adapter.

    [Fact]
    public async Task LeaveTenant_Unauthenticated_Returns401()
    {
        var result = await _sut.LeaveTenant(TestTenantId);

        result.Should().BeOfType<UnauthorizedResult>();
        await _membershipService.DidNotReceiveWithAnyArgs().LeaveTenantAsync(default, default, default);
    }

    [Fact]
    public async Task LeaveTenant_IdentityProviderPrincipal_Returns401()
    {
        // The sub of an identity-provider token is not a GroundUp user id, even when it parses as a GUID
        SetAuthenticatedExternalIdpUser(new[] { new Claim("sub", TestUserId.ToString()) });

        var result = await _sut.LeaveTenant(Guid.NewGuid());

        result.Should().BeOfType<UnauthorizedResult>();
        await _membershipService.DidNotReceiveWithAnyArgs().LeaveTenantAsync(default, default, default);
    }

    [Fact]
    public async Task LeaveTenant_PassesTenantAndAuthTimeToService()
    {
        var authTime = DateTimeOffset.UtcNow.AddHours(-1);
        SetGroundUpUser(withAuthTime: authTime);
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(TestTenantId, false, null)));
        var targetTenantId = Guid.NewGuid();

        await _sut.LeaveTenant(targetTenantId);

        await _membershipService.Received(1).LeaveTenantAsync(
            targetTenantId,
            Arg.Is<DateTimeOffset?>(d => d.HasValue && d.Value.ToUnixTimeSeconds() == authTime.ToUnixTimeSeconds()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveTenant_ServiceUnauthorized_Returns401()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Unauthorized());

        var result = await _sut.LeaveTenant(TestTenantId);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task LeaveTenant_NotAMember_Returns404WithError()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.NotFound("User is not a member of this tenant."));

        var result = await _sut.LeaveTenant(TestTenantId);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        GetAnonymousObjectProperties(notFound.Value!)["error"].Should().Be("User is not a member of this tenant.");
    }

    [Fact]
    public async Task LeaveTenant_LastAdmin_Returns403WithErrorCode()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Fail("Last admin", 403, "LAST_ADMIN"));

        var result = await _sut.LeaveTenant(TestTenantId);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        var body = GetAnonymousObjectProperties(objectResult.Value!);
        body["error"].Should().Be("Last admin");
        body["errorCode"].Should().Be("LAST_ADMIN");
    }

    [Fact]
    public async Task LeaveTenant_SaveConflict_Returns409()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Fail("A conflict occurred while leaving the tenant.", 409));

        var result = await _sut.LeaveTenant(TestTenantId);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        _authCookieWriter.DidNotReceive().ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task LeaveTenant_SessionEndedWithToken_WritesTenantlessCookieAndReportsNoTenant()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(null, true, "tenantless-token")));

        var result = await _sut.LeaveTenant(TestTenantId);

        var body = GetAnonymousObjectProperties(result.Should().BeOfType<OkObjectResult>().Subject.Value!);
        body["success"].Should().Be(true);
        body["currentTenantId"].Should().BeNull();
        body.Should().NotContainKey("noTenants");
        _authCookieWriter.Received(1).WriteAuthCookieAsync(Arg.Any<HttpContext>(), "tenantless-token");
        _authCookieWriter.DidNotReceive().ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task LeaveTenant_SessionEndedWithoutToken_ClearsCookieSoOldTokenIsNotKept()
    {
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(null, true, null)));

        var result = await _sut.LeaveTenant(TestTenantId);

        GetAnonymousObjectProperties(result.Should().BeOfType<OkObjectResult>().Subject.Value!)["currentTenantId"]
            .Should().BeNull();
        _authCookieWriter.Received(1).ClearAuthCookieAsync(Arg.Any<HttpContext>());
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LeaveTenant_SessionUnchanged_LeavesCookieAloneAndReportsCurrentTenant()
    {
        var currentTenantId = Guid.NewGuid();
        SetGroundUpUser();
        GivenLeaveResult(OperationResult<LeaveTenantResult>.Ok(new LeaveTenantResult(currentTenantId, false, null)));

        var result = await _sut.LeaveTenant(Guid.NewGuid());

        var body = GetAnonymousObjectProperties(result.Should().BeOfType<OkObjectResult>().Subject.Value!);
        body["success"].Should().Be(true);
        body["currentTenantId"].Should().Be(currentTenantId);
        _authCookieWriter.DidNotReceive().WriteAuthCookieAsync(Arg.Any<HttpContext>(), Arg.Any<string>());
        _authCookieWriter.DidNotReceive().ClearAuthCookieAsync(Arg.Any<HttpContext>());
    }

    private void SetGroundUpUser(DateTimeOffset? withAuthTime = null)
    {
        var claims = new List<Claim>
        {
            new("sub", TestUserId.ToString()),
            new("tid", TestTenantId.ToString())
        };
        if (withAuthTime is { } authTime)
        {
            claims.Add(new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString()));
        }

        SetAuthenticatedUser(claims.ToArray());
    }

    private void GivenLeaveResult(OperationResult<LeaveTenantResult> result)
        => _membershipService.LeaveTenantAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(result);

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
        await _authSessionService.DidNotReceiveWithAnyArgs().SetTenantForExternalUserAsync(default!, default, default);
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
        var newTenantId = Guid.NewGuid();
        GivenCreateResult(OperationResult<OrganizationProvisioningResult>.Ok(
            new OrganizationProvisioningResult(newTenantId, "Acme Corp", "acme-corp")));

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
            _membershipService,
            _organizationProvisioningService,
            Options.Create(_authOptions),
            Options.Create(_keycloakOptions),
            _antiforgery,
            Options.Create(new AntiforgeryOptions { Cookie = { Name = "CsrfToken" } }),
            _permissionService);

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
