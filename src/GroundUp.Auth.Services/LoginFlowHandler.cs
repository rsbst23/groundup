using System.Security.Claims;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Session;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services;

/// <summary>
/// Handles the <see cref="FlowType.Login"/> callback flow.
/// Resolves user memberships and routes to the correct scenario:
/// auto-join (zero memberships + default tenant), tenantless session (zero memberships + no default),
/// auto-select (single membership), tenantless session with a tenant picker (2+ memberships),
/// host-pinned login, or enterprise "not yet implemented" stub.
/// </summary>
internal sealed class LoginFlowHandler : IFlowHandler
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IIdTokenValidator _idTokenValidator;
    private readonly CallbackSessionGuard _sessionGuard;
    private readonly SignInUserResolver _userResolver;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly ISettingsService _settingsService;
    private readonly DefaultRoleAssigner _defaultRoleAssigner;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<LoginFlowHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginFlowHandler"/> class.
    /// </summary>
    public LoginFlowHandler(
        IIdentityProviderService identityProviderService,
        IIdTokenValidator idTokenValidator,
        CallbackSessionGuard sessionGuard,
        SignInUserResolver userResolver,
        IUserTenantRepository userTenantRepository,
        ITenantRepository tenantRepository,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IAuthFlowStateService authFlowStateService,
        ISettingsService settingsService,
        DefaultRoleAssigner defaultRoleAssigner,
        IHttpContextAccessor httpContextAccessor,
        TenantContext tenantContext,
        ILogger<LoginFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _idTokenValidator = idTokenValidator;
        _sessionGuard = sessionGuard;
        _userResolver = userResolver;
        _userTenantRepository = userTenantRepository;
        _tenantRepository = tenantRepository;
        _tokenService = tokenService;
        _authCookieWriter = authCookieWriter;
        _authFlowStateService = authFlowStateService;
        _settingsService = settingsService;
        _defaultRoleAssigner = defaultRoleAssigner;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public FlowType HandledFlowType => FlowType.Login;

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
            _logger.LogWarning("Login flow failed: code exchange returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Code exchange failed");
            return FlowResult.Error("CODE_EXCHANGE_FAILED", "Failed to exchange authorization code for tokens.");
        }

        // 2. Validate the ID token (signature, issuer, audience, expiry, nonce) before trusting it
        var idTokenResult = await _idTokenValidator.ValidateAsync(tokenResponse.IdToken, state.Nonce, state.Realm);
        if (!idTokenResult.Success || idTokenResult.Data is null)
        {
            _logger.LogWarning("Login flow failed: ID token rejected ({ErrorCode}) for state {StateId}",
                idTokenResult.ErrorCode, state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "ID token rejected");
            return FlowResult.Error(idTokenResult.ErrorCode ?? IdTokenErrorCodes.Invalid, idTokenResult.Message);
        }

        // The session is counted from the identity provider's auth_time, so a silent single
        // sign-on does not restart it. Refuse a session already past the System lifetime.
        var authTime = _sessionGuard.ResolveAuthTime(idTokenResult.Data);
        if (await _sessionGuard.CheckAsync(authTime, tenantId: null) is { } systemCapReached)
        {
            return systemCapReached;
        }

        // 3. Retrieve user information
        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);

        if (userInfo is null)
        {
            _logger.LogWarning("Login flow failed: userinfo retrieval returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "UserInfo retrieval failed");
            return FlowResult.Error("USERINFO_FAILED", "Failed to retrieve user information from identity provider.");
        }

        // 4. Resolve or create the user (claims a pending user whose email the IdP verified)
        var user = await _userResolver.ResolveAsync(userInfo);

        // 5. Check for enterprise host-resolved tenant (not yet implemented)
        if (context.HostResolvedTenant is { RealmName: not null })
        {
            _logger.LogInformation(
                "Login flow: enterprise tenant {TenantSlug} detected, returning not-yet-implemented for state {StateId}",
                context.HostResolvedTenant.Slug, state.Id);
            return FlowResult.Error("NOT_YET_IMPLEMENTED",
                "Enterprise tenant login is not yet implemented. This feature will be available in a future release.",
                httpStatus: 501);
        }

        // 6. Query active memberships
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(user.Id);
        if (!membershipsResult.Success || membershipsResult.Data is null)
        {
            _logger.LogError("Login flow: failed to query memberships for user {UserId}", user.Id);
            return FlowResult.Error("MEMBERSHIP_QUERY_FAILED", "Failed to query user memberships.");
        }

        var activeMemberships = membershipsResult.Data.Where(m => m.IsActive).ToList();

        // 7. Host-pinned scenarios
        if (context.HostResolvedTenant is not null)
        {
            return await HandleHostPinnedAsync(user, activeMemberships, context.HostResolvedTenant, tokenResponse.IdToken, authTime);
        }

        // 8. Non-host-pinned scenarios based on membership count
        return activeMemberships.Count switch
        {
            0 => await HandleZeroMembershipsAsync(user, membershipsResult.Data, tokenResponse.IdToken, authTime),
            1 => await HandleSingleMembershipAsync(user, activeMemberships[0], tokenResponse.IdToken, authTime),
            _ => await HandleMultipleMembershipsAsync(user, activeMemberships, tokenResponse, authTime)
        };
    }

    /// <summary>
    /// Handles the host-pinned login scenario where a specific tenant is resolved from the subdomain.
    /// </summary>
    private async Task<FlowResult> HandleHostPinnedAsync(
        UserDto user,
        List<UserTenantDto> activeMemberships,
        TenantDto hostTenant,
        string? idToken,
        DateTimeOffset authTime)
    {
        var membership = activeMemberships.FirstOrDefault(m => m.TenantId == hostTenant.Id);

        if (membership is null)
        {
            _logger.LogInformation(
                "Login flow: user {UserId} is not a member of host-pinned tenant {TenantSlug}",
                user.Id, hostTenant.Slug);
            return FlowResult.Error("ACCESS_DENIED",
                "You do not have access to this organization.", httpStatus: 403);
        }

        // User is a member — issue token scoped to the host-resolved tenant (if within its lifetime)
        if (await _sessionGuard.CheckAsync(authTime, hostTenant.Id) is { } tenantCapReached)
        {
            return tenantCapReached;
        }

        var token = await GenerateTokenWithAuthTimeAsync(user.Id, hostTenant.Id, authTime);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        await WriteCookieAsync(token);
        await WriteIdTokenCookieAsync(idToken);
        _logger.LogInformation(
            "Login flow: host-pinned auto-select for user {UserId} in tenant {TenantId}",
            user.Id, hostTenant.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the zero-active-memberships scenario: auto-join if a default tenant is configured,
    /// issue a tenantless session if none is configured, access-denied if the configured default
    /// tenant does not resolve to an active tenant (a configuration fault that should stay visible).
    /// A user who previously left the default tenant still has a deactivated membership row, and a
    /// user has only one row per tenant, so that row is reactivated instead of inserting a new one.
    /// </summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="allMemberships">All of the user's membership rows, active or not.</param>
    /// <param name="idToken">The raw ID token, kept for the logout hint cookie.</param>
    /// <param name="authTime">When the user last authenticated (from the ID token).</param>
    private async Task<FlowResult> HandleZeroMembershipsAsync(
        UserDto user,
        List<UserTenantDto> allMemberships,
        string? idToken,
        DateTimeOffset authTime)
    {
        // Check for default tenant configuration
        var defaultTenantSlugResult = await _settingsService.GetAsync<string>(
            "auth.application.default-tenant-slug");

        if (!defaultTenantSlugResult.Success
            || string.IsNullOrWhiteSpace(defaultTenantSlugResult.Data))
        {
            // Nothing to enter. The user is still signed in: a tenantless session lets them
            // create an organization (or accept an invitation later) instead of hitting a dead end.
            if (!await TryWriteTenantlessSessionAsync(user.Id, idToken, authTime))
            {
                return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
            }

            _logger.LogInformation(
                "Login flow: user {UserId} has zero memberships and no default tenant configured, issued tenantless session",
                user.Id);
            return FlowResult.NoCurrentTenant();
        }

        // Resolve default tenant by slug
        var tenantResult = await _tenantRepository.GetBySlugBypassFilterAsync(
            defaultTenantSlugResult.Data.Trim().ToLowerInvariant());

        if (!tenantResult.Success || tenantResult.Data is null || !tenantResult.Data.IsActive)
        {
            _logger.LogWarning(
                "Login flow: default tenant slug '{Slug}' does not resolve to an active tenant",
                defaultTenantSlugResult.Data);
            return FlowResult.Error("ACCESS_DENIED",
                "The default organization is not available. Please contact an administrator.",
                httpStatus: 403);
        }

        var defaultTenant = tenantResult.Data;

        // Check the default tenant's lifetime before joining, so a refused sign-in changes nothing.
        if (await _sessionGuard.CheckAsync(authTime, defaultTenant.Id) is { } tenantCapReached)
        {
            return tenantCapReached;
        }

        var previousMembership = allMemberships.FirstOrDefault(m => m.TenantId == defaultTenant.Id);
        if (previousMembership is not null)
        {
            // The user left this tenant earlier (or was invited to it and has not accepted yet).
            // Rejoin through the same row (unique per user and tenant). Leaving removed their roles,
            // so a former member gets the default role again.
            var reactivateResult = await _userTenantRepository.ReactivateMembershipAsync(user.Id, defaultTenant.Id);
            if (!reactivateResult.Success)
            {
                _logger.LogError(
                    "Login flow: failed to reactivate membership for user {UserId} in tenant {TenantId}",
                    user.Id, defaultTenant.Id);
                return FlowResult.Error("AUTO_JOIN_FAILED", "Failed to rejoin the default organization.");
            }

            await _defaultRoleAssigner.AssignAsync(user.Id, defaultTenant.Id);
        }
        else
        {
            // The membership repository stamps the current tenant onto new rows. During a sign-in
            // callback no tenant is selected yet, so point the context at the tenant being joined.
            _tenantContext.TenantId = defaultTenant.Id;

            // Auto-join: create membership
            var membershipDto = new UserTenantDto(
                Id: Guid.NewGuid(),
                UserId: user.Id,
                TenantId: defaultTenant.Id,
                ExternalUserId: user.ExternalUserId,
                Status: MembershipStatus.Active);

            var membershipResult = await _userTenantRepository.AddAsync(membershipDto);
            if (!membershipResult.Success)
            {
                _logger.LogError("Login flow: failed to create membership for user {UserId} in tenant {TenantId}",
                    user.Id, defaultTenant.Id);
                return FlowResult.Error("AUTO_JOIN_FAILED", "Failed to join the default organization.");
            }

            // Assign the default role (if configured)
            await _defaultRoleAssigner.AssignAsync(user.Id, defaultTenant.Id);
        }

        // Issue token scoped to the default tenant
        var token = await GenerateTokenWithAuthTimeAsync(user.Id, defaultTenant.Id, authTime);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        await WriteCookieAsync(token);
        await WriteIdTokenCookieAsync(idToken);
        _logger.LogInformation(
            "Login flow: auto-joined user {UserId} to default tenant {TenantId}",
            user.Id, defaultTenant.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the single-membership scenario: auto-select the one tenant.
    /// </summary>
    private async Task<FlowResult> HandleSingleMembershipAsync(
        UserDto user,
        UserTenantDto membership,
        string? idToken,
        DateTimeOffset authTime)
    {
        if (await _sessionGuard.CheckAsync(authTime, membership.TenantId) is { } tenantCapReached)
        {
            return tenantCapReached;
        }

        var token = await GenerateTokenWithAuthTimeAsync(user.Id, membership.TenantId, authTime);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        await WriteCookieAsync(token);
        await WriteIdTokenCookieAsync(idToken);
        _logger.LogInformation(
            "Login flow: auto-selected single tenant {TenantId} for user {UserId}",
            membership.TenantId, user.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the multi-membership scenario: return tenant selection list and
    /// write a tenantless GroundUp token as the auth cookie.
    /// Enterprise tenants (RealmName ≠ null) are excluded from the picker.
    /// </summary>
    private async Task<FlowResult> HandleMultipleMembershipsAsync(
        UserDto user,
        List<UserTenantDto> activeMemberships,
        TokenResponseDto tokenResponse,
        DateTimeOffset authTime)
    {
        // Load tenant details for the memberships to filter by RealmName
        var membershipTenantIds = activeMemberships.Select(m => m.TenantId).ToList();
        var tenantsResult = await _tenantRepository.GetByIdsBypassFilterAsync(membershipTenantIds);

        if (!tenantsResult.Success || tenantsResult.Data is null)
        {
            _logger.LogError("Login flow: failed to load tenant details for user {UserId}", user.Id);
            return FlowResult.Error("TENANT_LOOKUP_FAILED", "Failed to load organization details.");
        }

        // Exclude enterprise tenants (RealmName ≠ null) from the picker
        var standardTenants = tenantsResult.Data
            .Where(t => t.RealmName is null)
            .ToList();

        // If after filtering we have 0 standard tenants, the user can only reach enterprise tenants
        if (standardTenants.Count == 0)
        {
            _logger.LogInformation(
                "Login flow: user {UserId} has memberships only in enterprise tenants, access denied via standard login",
                user.Id);
            return FlowResult.Error("ACCESS_DENIED",
                "Your organizations require enterprise login. Please use the organization's dedicated login page.",
                httpStatus: 403);
        }

        // If after filtering we have exactly 1 standard tenant, auto-select it
        if (standardTenants.Count == 1)
        {
            if (await _sessionGuard.CheckAsync(authTime, standardTenants[0].Id) is { } tenantCapReached)
            {
                return tenantCapReached;
            }

            var token = await GenerateTokenWithAuthTimeAsync(user.Id, standardTenants[0].Id, authTime);
            if (token is null)
            {
                return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
            }

            await WriteCookieAsync(token);
            await WriteIdTokenCookieAsync(tokenResponse.IdToken);
            _logger.LogInformation(
                "Login flow: auto-selected single standard tenant {TenantId} for user {UserId} (enterprise tenants filtered)",
                standardTenants[0].Id, user.Id);
            return FlowResult.Success(token);
        }

        // 2+ standard tenants: issue a tenantless GroundUp token (identity only, no tid, no roles).
        // The session stays tenantless until POST /auth/set-tenant selects a tenant. The Keycloak
        // access token is not kept: it is short-lived and cannot be used to look up the user's roles.
        if (!await TryWriteTenantlessSessionAsync(user.Id, tokenResponse.IdToken, authTime))
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        var tenantList = standardTenants
            .Select(t => new TenantListItemDto(t.Id, t.Name, null))
            .ToList();

        _logger.LogInformation(
            "Login flow: tenant selection required for user {UserId}, {Count} standard tenants available",
            user.Id, tenantList.Count);

        return FlowResult.TenantSelectionRequired(tenantList);
    }

    /// <summary>
    /// Issues a tenantless token carrying <paramref name="authTime"/> and writes it, plus the id_token
    /// cookie used for logout, to the response. Returns false when the token could not be generated.
    /// </summary>
    private async Task<bool> TryWriteTenantlessSessionAsync(Guid userId, string? idToken, DateTimeOffset authTime)
    {
        var token = await _tokenService.GenerateTenantlessTokenAsync(userId, new[] { AuthTimeClaim(authTime) });
        if (token is null)
        {
            return false;
        }

        await WriteCookieAsync(token);
        await WriteIdTokenCookieAsync(idToken);
        return true;
    }

    /// <summary>
    /// Generates a tenant-scoped GroundUp JWT carrying <paramref name="authTime"/> (from the ID token).
    /// </summary>
    private Task<string?> GenerateTokenWithAuthTimeAsync(Guid userId, Guid tenantId, DateTimeOffset authTime) =>
        _tokenService.GenerateTokenAsync(userId, tenantId, new[] { AuthTimeClaim(authTime) });

    private static Claim AuthTimeClaim(DateTimeOffset authTime) =>
        new("auth_time", authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64);

    /// <summary>
    /// Writes the authentication cookie using the current HTTP context.
    /// </summary>
    private async Task WriteCookieAsync(string token)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            await _authCookieWriter.WriteAuthCookieAsync(httpContext, token);
        }
    }

    /// <summary>
    /// Writes the Keycloak id_token to its secondary cookie for use as id_token_hint at logout.
    /// </summary>
    private async Task WriteIdTokenCookieAsync(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return;
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            await _authCookieWriter.WriteIdTokenCookieAsync(httpContext, idToken);
        }
    }
}
