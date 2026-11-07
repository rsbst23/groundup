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
/// Handles the <see cref="FlowType.SimpleSignup"/> callback flow.
/// Registers or resolves the user, then auto-joins them to the configured default tenant.
/// Simpler than <see cref="NewOrganizationFlowHandler"/> — no tenant creation, no slug retries,
/// no transaction needed (just a membership insert).
/// </summary>
public sealed class SimpleSignupFlowHandler : IFlowHandler
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

    /// <summary>
    /// Initializes a new instance of <see cref="SimpleSignupFlowHandler"/>.
    /// </summary>
    public SimpleSignupFlowHandler(
        IIdentityProviderService identityProviderService,
        IUserRepository userRepository,
        IUserTenantRepository userTenantRepository,
        ITenantRepository tenantRepository,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IAuthFlowStateService authFlowStateService,
        ISettingsService settingsService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SimpleSignupFlowHandler> logger)
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
    public FlowType HandledFlowType => FlowType.SimpleSignup;

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
            _logger.LogWarning("SimpleSignup flow failed: code exchange returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Code exchange failed");
            return FlowResult.Error("CODE_EXCHANGE_FAILED", "Failed to exchange authorization code for tokens.");
        }

        // 2. Validate id_token nonce
        if (!ValidateNonce(tokenResponse.IdToken, state.Nonce))
        {
            _logger.LogWarning("SimpleSignup flow failed: nonce mismatch for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Nonce mismatch");
            return FlowResult.Error("NONCE_MISMATCH", "ID token nonce does not match the expected value.");
        }

        // 3. Retrieve user information
        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);

        if (userInfo is null)
        {
            _logger.LogWarning("SimpleSignup flow failed: userinfo retrieval returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "UserInfo retrieval failed");
            return FlowResult.Error("USERINFO_FAILED", "Failed to retrieve user information from identity provider.");
        }

        // 4. Resolve or create user by ExternalUserId (sub)
        var user = await ResolveOrCreateUserAsync(userInfo);

        // 5. Read the default-tenant-slug setting
        var defaultTenantSlugResult = await _settingsService.GetAsync<string>(
            "auth.application.default-tenant-slug");

        if (!defaultTenantSlugResult.Success
            || string.IsNullOrWhiteSpace(defaultTenantSlugResult.Data))
        {
            _logger.LogWarning("SimpleSignup flow failed: no default tenant slug configured for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "No default tenant configured");
            return FlowResult.Error("NO_DEFAULT_TENANT",
                "Simple sign-up is not available — no default tenant has been configured.");
        }

        // 6. Lookup default tenant by slug
        var tenantResult = await _tenantRepository.GetBySlugBypassFilterAsync(
            defaultTenantSlugResult.Data.Trim().ToLowerInvariant());

        if (!tenantResult.Success || tenantResult.Data is null || !tenantResult.Data.IsActive)
        {
            _logger.LogWarning(
                "SimpleSignup flow failed: default tenant slug '{Slug}' does not resolve to an active tenant for state {StateId}",
                defaultTenantSlugResult.Data, state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Default tenant not found or inactive");
            return FlowResult.Error("NO_DEFAULT_TENANT",
                "The configured default tenant is not available. Please contact an administrator.");
        }

        var defaultTenant = tenantResult.Data;

        // 7. Check if user is already a member (idempotent)
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(user.Id);
        if (membershipsResult.Success && membershipsResult.Data is not null)
        {
            var existingMembership = membershipsResult.Data
                .FirstOrDefault(m => m.TenantId == defaultTenant.Id && m.IsActive);

            if (existingMembership is null)
            {
                // 8. Create UserTenant membership
                var membershipDto = new UserTenantDto(
                    Id: Guid.NewGuid(),
                    UserId: user.Id,
                    TenantId: defaultTenant.Id,
                    ExternalUserId: user.ExternalUserId,
                    IsActive: true);

                var membershipResult = await _userTenantRepository.AddAsync(membershipDto);
                if (!membershipResult.Success)
                {
                    _logger.LogError(
                        "SimpleSignup flow failed: could not create membership for user {UserId} in tenant {TenantId}",
                        user.Id, defaultTenant.Id);
                    return FlowResult.Error("MEMBERSHIP_CREATION_FAILED",
                        "Failed to join the default organization.");
                }

                // 9. Assign default role (if configured)
                await AssignDefaultRoleAsync(user.Id, defaultTenant.Id);

                _logger.LogInformation(
                    "SimpleSignup flow: created membership for user {UserId} in default tenant {TenantId}",
                    user.Id, defaultTenant.Id);
            }
            else
            {
                _logger.LogInformation(
                    "SimpleSignup flow: user {UserId} already a member of default tenant {TenantId}, skipping membership creation",
                    user.Id, defaultTenant.Id);
            }
        }

        // 10. Generate token scoped to default tenant with auth_time = now
        var authTimeClaim = new Claim(
            "auth_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64);

        var token = await _tokenService.GenerateTokenAsync(user.Id, defaultTenant.Id, new[] { authTimeClaim });

        if (token is null)
        {
            _logger.LogError("SimpleSignup flow failed: token generation returned null for user {UserId}", user.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Token generation failed");
            return FlowResult.Error("TOKEN_GENERATION_FAILED", "Failed to generate authentication token.");
        }

        // 11. Write auth cookie
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            _authCookieWriter.WriteAuthCookie(httpContext, token);
        }

        _logger.LogInformation(
            "SimpleSignup flow completed: UserId={UserId}, TenantId={TenantId}",
            user.Id, defaultTenant.Id);

        return FlowResult.Success(token);
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
            _logger.LogInformation("SimpleSignup flow: created new user {UserId} for external ID {ExternalUserId}",
                createResult.Data.Id, userInfo.ExternalUserId);
            return createResult.Data;
        }

        // Handle unique-constraint violation (concurrent first-login): re-read existing user
        _logger.LogInformation(
            "SimpleSignup flow: user creation conflict, re-reading for external ID {ExternalUserId}",
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
    private async Task AssignDefaultRoleAsync(Guid userId, Guid tenantId)
    {
        _ = userId;    // Will be used when role lookup by name is available
        _ = tenantId;  // Will be used when role lookup by name is available

        var defaultRoleResult = await _settingsService.GetAsync<string>("auth.application.default-role");
        if (!defaultRoleResult.Success || string.IsNullOrWhiteSpace(defaultRoleResult.Data))
        {
            _logger.LogDebug("SimpleSignup flow: no default role configured, skipping role assignment");
            return;
        }

        // The default role setting contains a role name. The role must already exist in the tenant.
        // Without a GetByNameAsync method on IRoleRepository, we cannot directly look up the role.
        // This will be wired when role lookup by name is available. For now, log the intent.
        _logger.LogDebug(
            "SimpleSignup flow: default role '{RoleName}' configured but role lookup by name " +
            "within tenant is not yet available. Role assignment skipped.",
            defaultRoleResult.Data);
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
}
