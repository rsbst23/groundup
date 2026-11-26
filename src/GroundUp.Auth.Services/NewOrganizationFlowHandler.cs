using System.Security.Claims;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Session;
using GroundUp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services;

/// <summary>
/// Handles the <see cref="FlowType.NewOrganization"/> callback flow.
/// Authenticates the founding user via the identity provider, resolves or creates the GroundUp user,
/// delegates organization creation to <see cref="IOrganizationProvisioningService"/>, then issues a token.
/// </summary>
internal sealed class NewOrganizationFlowHandler : IFlowHandler
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IIdTokenValidator _idTokenValidator;
    private readonly CallbackSessionGuard _sessionGuard;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly SignInUserResolver _userResolver;
    private readonly IOrganizationProvisioningService _organizationProvisioningService;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<NewOrganizationFlowHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="NewOrganizationFlowHandler"/>.
    /// </summary>
    public NewOrganizationFlowHandler(
        IIdentityProviderService identityProviderService,
        IIdTokenValidator idTokenValidator,
        CallbackSessionGuard sessionGuard,
        IAuthFlowStateService authFlowStateService,
        SignInUserResolver userResolver,
        IOrganizationProvisioningService organizationProvisioningService,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IHttpContextAccessor httpContextAccessor,
        TenantContext tenantContext,
        ILogger<NewOrganizationFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _idTokenValidator = idTokenValidator;
        _sessionGuard = sessionGuard;
        _authFlowStateService = authFlowStateService;
        _userResolver = userResolver;
        _organizationProvisioningService = organizationProvisioningService;
        _tokenService = tokenService;
        _authCookieWriter = authCookieWriter;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public FlowType HandledFlowType => FlowType.NewOrganization;

    /// <inheritdoc />
    public async Task<FlowResult> HandleCallbackAsync(FlowCallbackContext context)
    {
        var state = context.ConsumedState;

        // 1. Exchange authorization code for tokens
        var tokenResponse = await _identityProviderService.ExchangeCodeForTokensAsync(
            context.AuthorizationCode,
            context.RedirectUri,
            state.Realm,
            codeVerifier: context.CodeVerifier);

        if (tokenResponse is null)
        {
            _logger.LogWarning("NewOrganization flow failed: code exchange returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Code exchange failed");
            return FlowResult.Error("CODE_EXCHANGE_FAILED", "Failed to exchange authorization code for tokens.");
        }

        // 2. Validate the ID token (signature, issuer, audience, expiry, nonce) before trusting it
        var idTokenResult = await _idTokenValidator.ValidateAsync(tokenResponse.IdToken, state.Nonce, state.Realm);
        if (!idTokenResult.Success || idTokenResult.Data is null)
        {
            _logger.LogWarning("NewOrganization flow failed: ID token rejected ({ErrorCode}) for state {StateId}",
                idTokenResult.ErrorCode, state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "ID token rejected");
            return FlowResult.Error(idTokenResult.ErrorCode ?? IdTokenErrorCodes.Invalid, idTokenResult.Message);
        }

        // Count the session from the identity provider's auth_time. A new organization has no
        // tenant-level session settings yet, so the System lifetime applies. Checked before the
        // organization is created, so a refused sign-in creates nothing.
        var authTime = _sessionGuard.ResolveAuthTime(idTokenResult.Data);
        if (await _sessionGuard.CheckAsync(authTime, tenantId: null) is { } systemCapReached)
        {
            return systemCapReached;
        }

        // 3. Retrieve user information
        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);

        if (userInfo is null)
        {
            _logger.LogWarning("NewOrganization flow failed: userinfo retrieval returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "UserInfo retrieval failed");
            return FlowResult.Error("USERINFO_FAILED", "Failed to retrieve user information from identity provider.");
        }

        // 4. Resolve or create the founding user (claims a pending user whose email the IdP verified).
        //    This happens before provisioning: the provisioning service never creates users.
        var userId = (await _userResolver.ResolveAsync(userInfo)).Id;

        // 5. Provision the organization (tenant, membership, TenantAdmin role, role assignment)
        var organizationName = state.OrganizationName ?? "Organization";
        var provisioningResult = await _organizationProvisioningService.ProvisionAsync(
            new OrganizationProvisioningRequest(userId, userInfo.ExternalUserId, organizationName));

        if (!provisioningResult.Success || provisioningResult.Data is null)
        {
            var errorCode = provisioningResult.ErrorCode ?? "ORGANIZATION_PROVISIONING_FAILED";
            _logger.LogError(
                "NewOrganization flow failed: provisioning failed for org '{OrgName}' with {ErrorCode}",
                organizationName, errorCode);
            await _authFlowStateService.MarkFailedAsync(state.Id, $"Organization provisioning failed: {errorCode}");
            return FlowResult.Error(errorCode, provisioningResult.Message, provisioningResult.StatusCode);
        }

        var tenantId = provisioningResult.Data.TenantId;

        // The provisioning service restores the tenant context it found. Point it at the new
        // tenant so token generation resolves the founder's roles in that tenant.
        _tenantContext.TenantId = tenantId;

        // 6. Generate GroundUp JWT token carrying the identity provider's auth_time
        var authTimeClaim = new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64);
        var token = await _tokenService.GenerateTokenAsync(userId, tenantId, new[] { authTimeClaim });

        if (token is null)
        {
            _logger.LogError("NewOrganization flow failed: token generation returned null for user {UserId}", userId);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Token generation failed");
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        // 7. Write the authentication cookie
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            await _authCookieWriter.WriteAuthCookieAsync(httpContext, token);
            await _authCookieWriter.WriteIdTokenCookieAsync(httpContext, tokenResponse.IdToken ?? string.Empty);
        }

        _logger.LogInformation(
            "NewOrganization flow completed: TenantId={TenantId}, UserId={UserId}",
            tenantId, userId);

        return FlowResult.Success(token);
    }
}
