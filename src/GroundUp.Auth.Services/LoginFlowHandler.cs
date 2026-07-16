using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services;

/// <summary>
/// Handles the <see cref="FlowType.Login"/> callback flow.
/// Resolves user memberships and routes to the correct scenario:
/// auto-join (zero memberships + default tenant), access-denied (zero memberships + no default),
/// auto-select (single membership), tenant-selection picker (2+ memberships),
/// host-pinned login, or enterprise "not yet implemented" stub.
/// </summary>
public sealed class LoginFlowHandler : IFlowHandler
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

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginFlowHandler"/> class.
    /// </summary>
    public LoginFlowHandler(
        IIdentityProviderService identityProviderService,
        IUserRepository userRepository,
        IUserTenantRepository userTenantRepository,
        ITenantRepository tenantRepository,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IAuthFlowStateService authFlowStateService,
        ISettingsService settingsService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<LoginFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _userRepository = userRepository;
        _userTenantRepository = userTenantRepository;
        _tenantRepository = tenantRepository;
        _tokenService = tokenService;
        _authCookieWriter = authCookieWriter;
        _authFlowStateService = authFlowStateService;
        _settingsService = settingsService;
        _httpContextAccessor = httpContextAccessor;
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

        // 2. Validate id_token nonce
        if (!ValidateNonce(tokenResponse.IdToken, state.Nonce))
        {
            _logger.LogWarning("Login flow failed: nonce mismatch for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Nonce mismatch");
            return FlowResult.Error("NONCE_MISMATCH", "ID token nonce does not match the expected value.");
        }

        // 3. Retrieve user information
        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);

        if (userInfo is null)
        {
            _logger.LogWarning("Login flow failed: userinfo retrieval returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "UserInfo retrieval failed");
            return FlowResult.Error("USERINFO_FAILED", "Failed to retrieve user information from identity provider.");
        }

        // 4. Resolve or create user by ExternalUserId (sub)
        var user = await ResolveOrCreateUserAsync(userInfo);

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
            return await HandleHostPinnedAsync(user, activeMemberships, context.HostResolvedTenant);
        }

        // 8. Non-host-pinned scenarios based on membership count
        return activeMemberships.Count switch
        {
            0 => await HandleZeroMembershipsAsync(user),
            1 => await HandleSingleMembershipAsync(user, activeMemberships[0]),
            _ => await HandleMultipleMembershipsAsync(user, activeMemberships, tokenResponse)
        };
    }

    /// <summary>
    /// Handles the host-pinned login scenario where a specific tenant is resolved from the subdomain.
    /// </summary>
    private async Task<FlowResult> HandleHostPinnedAsync(
        UserDto user,
        List<UserTenantDto> activeMemberships,
        TenantDto hostTenant)
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

        // User is a member — issue token scoped to the host-resolved tenant
        var token = await GenerateTokenWithAuthTimeAsync(user.Id, hostTenant.Id);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        WriteCookie(token);
        _logger.LogInformation(
            "Login flow: host-pinned auto-select for user {UserId} in tenant {TenantId}",
            user.Id, hostTenant.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the zero-memberships scenario: auto-join if default tenant configured, else access-denied.
    /// </summary>
    private async Task<FlowResult> HandleZeroMembershipsAsync(UserDto user)
    {
        // Check for default tenant configuration
        var defaultTenantSlugResult = await _settingsService.GetAsync<string>(
            "auth.application.default-tenant-slug");

        if (!defaultTenantSlugResult.Success
            || string.IsNullOrWhiteSpace(defaultTenantSlugResult.Data))
        {
            _logger.LogInformation(
                "Login flow: user {UserId} has zero memberships and no default tenant configured",
                user.Id);
            return FlowResult.Error("ACCESS_DENIED",
                "You do not have access to any organization. Please contact an administrator.",
                httpStatus: 403);
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

        // Auto-join: create membership
        var membershipDto = new UserTenantDto(
            Id: Guid.NewGuid(),
            UserId: user.Id,
            TenantId: defaultTenant.Id,
            ExternalUserId: user.ExternalUserId,
            IsActive: true);

        var membershipResult = await _userTenantRepository.AddAsync(membershipDto);
        if (!membershipResult.Success)
        {
            _logger.LogError("Login flow: failed to create membership for user {UserId} in tenant {TenantId}",
                user.Id, defaultTenant.Id);
            return FlowResult.Error("AUTO_JOIN_FAILED", "Failed to join the default organization.");
        }

        // Assign default role from cascade (if configured)
        await AssignDefaultRoleAsync(user.Id, defaultTenant.Id);

        // Issue token scoped to the default tenant
        var token = await GenerateTokenWithAuthTimeAsync(user.Id, defaultTenant.Id);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        WriteCookie(token);
        _logger.LogInformation(
            "Login flow: auto-joined user {UserId} to default tenant {TenantId}",
            user.Id, defaultTenant.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the single-membership scenario: auto-select the one tenant.
    /// </summary>
    private async Task<FlowResult> HandleSingleMembershipAsync(UserDto user, UserTenantDto membership)
    {
        var token = await GenerateTokenWithAuthTimeAsync(user.Id, membership.TenantId);
        if (token is null)
        {
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        WriteCookie(token);
        _logger.LogInformation(
            "Login flow: auto-selected single tenant {TenantId} for user {UserId}",
            membership.TenantId, user.Id);
        return FlowResult.Success(token);
    }

    /// <summary>
    /// Handles the multi-membership scenario: return tenant selection list,
    /// retain Keycloak token as auth cookie (no GroundUp token issued).
    /// Enterprise tenants (RealmName ≠ null) are excluded from the picker.
    /// </summary>
    private async Task<FlowResult> HandleMultipleMembershipsAsync(
        UserDto user,
        List<UserTenantDto> activeMemberships,
        TokenResponseDto tokenResponse)
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
            var token = await GenerateTokenWithAuthTimeAsync(user.Id, standardTenants[0].Id);
            if (token is null)
            {
                return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
            }

            WriteCookie(token);
            _logger.LogInformation(
                "Login flow: auto-selected single standard tenant {TenantId} for user {UserId} (enterprise tenants filtered)",
                standardTenants[0].Id, user.Id);
            return FlowResult.Success(token);
        }

        // 2+ standard tenants: write Keycloak token as auth cookie (no GroundUp token issued).
        // The JwtAuthenticationMiddleware's IdP-fallback path will authenticate this token
        // as an identity-only principal (no tid) until POST /auth/set-tenant selects a tenant.
        WriteCookie(tokenResponse.AccessToken);

        var tenantList = standardTenants
            .Select(t => new TenantListItemDto(t.Id, t.Name, null))
            .ToList();

        _logger.LogInformation(
            "Login flow: tenant selection required for user {UserId}, {Count} standard tenants available",
            user.Id, tenantList.Count);

        return FlowResult.TenantSelectionRequired(tenantList);
    }

    /// <summary>
    /// Resolves an existing user by ExternalUserId or creates a new one.
    /// Handles unique-constraint violations on ExternalUserId by re-reading the existing user.
    /// </summary>
    private async Task<UserDto> ResolveOrCreateUserAsync(ExternalUserInfo userInfo)
    {
        var existingResult = await _userRepository.GetByExternalUserIdAsync(userInfo.ExternalUserId);
        if (existingResult.Success && existingResult.Data is not null)
        {
            return existingResult.Data;
        }

        // Create new user
        var newUserDto = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: userInfo.ExternalUserId,
            Email: userInfo.Email,
            DisplayName: userInfo.DisplayName ?? userInfo.Email,
            IsActive: true);

        var createResult = await _userRepository.AddAsync(newUserDto);
        if (createResult.Success && createResult.Data is not null)
        {
            _logger.LogInformation("Login flow: created new user {UserId} for external ID {ExternalUserId}",
                createResult.Data.Id, userInfo.ExternalUserId);
            return createResult.Data;
        }

        // Handle unique-constraint violation (concurrent first-login): re-read existing user
        _logger.LogInformation(
            "Login flow: user creation conflict, re-reading for external ID {ExternalUserId}",
            userInfo.ExternalUserId);
        var retryResult = await _userRepository.GetByExternalUserIdAsync(userInfo.ExternalUserId);
        if (retryResult.Success && retryResult.Data is not null)
        {
            return retryResult.Data;
        }

        throw new InvalidOperationException(
            $"Failed to resolve or create user for external ID '{userInfo.ExternalUserId}'.");
    }

    /// <summary>
    /// Assigns the default role (from cascade settings) to the user in the specified tenant.
    /// If no default role is configured or the role cannot be resolved, no role is assigned.
    /// </summary>
    /// <param name="userId">The user to assign the role to.</param>
    /// <param name="tenantId">The tenant in which to assign the role.</param>
    private async Task AssignDefaultRoleAsync(Guid userId, Guid tenantId)
    {
        _ = userId;    // Will be used when role lookup by name is available
        _ = tenantId;  // Will be used when role lookup by name is available

        var defaultRoleResult = await _settingsService.GetAsync<string>("auth.application.default-role");
        if (!defaultRoleResult.Success || string.IsNullOrWhiteSpace(defaultRoleResult.Data))
        {
            _logger.LogDebug("Login flow: no default role configured, skipping role assignment for auto-join");
            return;
        }

        // The default role setting contains a role name. The role must already exist in the tenant.
        // Without a GetByNameAsync method on IRoleRepository, we cannot directly look up the role.
        // This will be wired when role lookup by name is available. For now, log the intent.
        _logger.LogDebug(
            "Login flow: default role '{RoleName}' configured for auto-join but role lookup by name " +
            "within tenant is not yet available. Role assignment skipped.",
            defaultRoleResult.Data);
    }

    /// <summary>
    /// Generates a GroundUp JWT token with the auth_time claim set to current UTC time.
    /// </summary>
    private async Task<string?> GenerateTokenWithAuthTimeAsync(Guid userId, Guid tenantId)
    {
        var authTimeClaim = new Claim(
            "auth_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64);

        return await _tokenService.GenerateTokenAsync(userId, tenantId, new[] { authTimeClaim });
    }

    /// <summary>
    /// Validates the nonce claim in the id_token against the expected nonce stored in AuthFlowState.
    /// Uses manual JWT payload decoding (no signature verification — IDP already validated the token).
    /// </summary>
    private static bool ValidateNonce(string? idToken, string expectedNonce)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return false;
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var payload = parts[1];
            // Add padding for Base64URL to Base64 conversion
            var remainder = payload.Length % 4;
            var padded = remainder switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var base64 = padded.Replace('-', '+').Replace('_', '/');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("nonce", out var nonceElement))
            {
                return string.Equals(nonceElement.GetString(), expectedNonce, StringComparison.Ordinal);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the authentication cookie using the current HTTP context.
    /// </summary>
    private void WriteCookie(string token)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            _authCookieWriter.WriteAuthCookie(httpContext, token);
        }
    }
}
