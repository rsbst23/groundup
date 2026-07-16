using System.Security.Claims;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Data.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services;

/// <summary>
/// Handles the <see cref="FlowType.NewOrganization"/> callback flow.
/// Atomically creates a new tenant, resolves or creates the founding user,
/// creates the membership and TenantAdmin role assignment, then issues a token.
/// </summary>
public sealed class NewOrganizationFlowHandler : IFlowHandler
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
    private readonly TenantContext _tenantContext;
    private readonly ILogger<NewOrganizationFlowHandler> _logger;

    /// <summary>
    /// Maximum number of slug disambiguation retries before giving up.
    /// </summary>
    private const int MaxSlugRetries = 5;

    /// <summary>
    /// Initializes a new instance of <see cref="NewOrganizationFlowHandler"/>.
    /// </summary>
    public NewOrganizationFlowHandler(
        IIdentityProviderService identityProviderService,
        IAuthFlowStateService authFlowStateService,
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IUserTenantRepository userTenantRepository,
        IUserRoleRepository userRoleRepository,
        IRoleRepository roleRepository,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IHttpContextAccessor httpContextAccessor,
        TenantContext tenantContext,
        ILogger<NewOrganizationFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _authFlowStateService = authFlowStateService;
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _userTenantRepository = userTenantRepository;
        _userRoleRepository = userRoleRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
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

        // 2. Validate id_token nonce
        if (!ValidateNonce(tokenResponse.IdToken, state.Nonce))
        {
            _logger.LogWarning("NewOrganization flow failed: nonce mismatch for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "Nonce mismatch");
            return FlowResult.Error("NONCE_MISMATCH", "ID token nonce does not match the expected value.");
        }

        // 3. Retrieve user information
        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);

        if (userInfo is null)
        {
            _logger.LogWarning("NewOrganization flow failed: userinfo retrieval returned null for state {StateId}", state.Id);
            await _authFlowStateService.MarkFailedAsync(state.Id, "UserInfo retrieval failed");
            return FlowResult.Error("USERINFO_FAILED", "Failed to retrieve user information from identity provider.");
        }

        // 4. Derive slug from organization name
        var organizationName = state.OrganizationName ?? "Organization";
        var baseSlug = SlugGenerator.FromOrganizationName(organizationName);

        // 5. Retry loop wrapping entire transaction on slug unique-constraint violation
        Guid tenantId = Guid.Empty;
        Guid userId = Guid.Empty;

        for (var attempt = 1; attempt <= MaxSlugRetries; attempt++)
        {
            var slug = attempt == 1 ? baseSlug : SlugGenerator.Disambiguate(baseSlug, attempt);

            try
            {
                var transactionResult = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
                {
                    // Create tenant
                    var newTenantId = Guid.NewGuid();
                    var tenantDto = new TenantDto(
                        Id: newTenantId,
                        Name: organizationName,
                        Slug: slug,
                        TenantType: TenantType.Standard,
                        OnboardingMode: OnboardingMode.InviteOnly,
                        ParentTenantId: null,
                        RealmName: null,
                        IsActive: true);

                    var tenantResult = await _tenantRepository.AddAsync(tenantDto, ct);
                    if (!tenantResult.Success)
                    {
                        throw new SlugCollisionException(slug);
                    }

                    tenantId = tenantResult.Data!.Id;

                    // Set the tenant context so tenant-scoped repositories use the new tenant
                    _tenantContext.TenantId = tenantId;

                    // Resolve or create user by ExternalUserId (sub claim)
                    userId = await ResolveOrCreateUserAsync(userInfo, ct);

                    // Create UserTenant membership
                    var membershipDto = new UserTenantDto(
                        Id: Guid.NewGuid(),
                        UserId: userId,
                        TenantId: tenantId,
                        ExternalUserId: userInfo.ExternalUserId,
                        IsActive: true);

                    await _userTenantRepository.AddAsync(membershipDto, ct);

                    // Create TenantAdmin role (tenant-scoped, IsSystem=true)
                    var roleDto = new RoleDto(
                        Id: Guid.NewGuid(),
                        Name: AuthRoleNames.TenantAdmin,
                        Description: "Full access within this tenant",
                        RoleType: RoleType.Application,
                        TenantId: tenantId,
                        IsSystem: true);

                    var roleResult = await _roleRepository.AddAsync(roleDto, ct);
                    var roleId = roleResult.Data!.Id;

                    // Assign TenantAdmin to the founding user
                    var userRoleDto = new UserRoleDto(
                        Id: Guid.NewGuid(),
                        UserId: userId,
                        RoleId: roleId,
                        TenantId: tenantId);

                    await _userRoleRepository.AddAsync(userRoleDto, ct);
                });

                if (transactionResult.Success)
                {
                    break; // Transaction committed successfully
                }

                // If the transaction failed due to a slug collision, retry
                if (attempt == MaxSlugRetries)
                {
                    _logger.LogError("NewOrganization flow failed: max slug retries exhausted for org '{OrgName}'", organizationName);
                    await _authFlowStateService.MarkFailedAsync(state.Id, "Slug collision exhausted retries");
                    return FlowResult.Error("SLUG_COLLISION", "Unable to create organization — slug unavailable after retries.");
                }
            }
            catch (SlugCollisionException)
            {
                if (attempt == MaxSlugRetries)
                {
                    _logger.LogError("NewOrganization flow failed: max slug retries exhausted for org '{OrgName}'", organizationName);
                    await _authFlowStateService.MarkFailedAsync(state.Id, "Slug collision exhausted retries");
                    return FlowResult.Error("SLUG_COLLISION", "Unable to create organization — slug unavailable after retries.");
                }

                _logger.LogDebug("Slug collision on '{Slug}', retrying with attempt {Attempt}", slug, attempt + 1);
                continue;
            }
        }

        // 6. Generate GroundUp JWT token with auth_time = now
        var authTimeClaim = new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64);
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
            _authCookieWriter.WriteAuthCookie(httpContext, token);
        }

        _logger.LogInformation(
            "NewOrganization flow completed: TenantId={TenantId}, UserId={UserId}",
            tenantId, userId);

        return FlowResult.Success(token);
    }

    /// <summary>
    /// Resolves an existing user by ExternalUserId or creates a new one.
    /// Handles unique-constraint violations on ExternalUserId by re-reading the existing user.
    /// </summary>
    private async Task<Guid> ResolveOrCreateUserAsync(ExternalUserInfo userInfo, CancellationToken ct)
    {
        // Try to find existing user by external sub
        var existingResult = await _userRepository.GetByExternalUserIdAsync(userInfo.ExternalUserId, ct);

        if (existingResult.Success && existingResult.Data is not null)
        {
            return existingResult.Data.Id;
        }

        // Create new user
        var newUserDto = new UserDto(
            Id: Guid.NewGuid(),
            ExternalUserId: userInfo.ExternalUserId,
            Email: userInfo.Email,
            DisplayName: userInfo.DisplayName ?? userInfo.Email,
            IsActive: true);

        try
        {
            var createResult = await _userRepository.AddAsync(newUserDto, ct);
            if (createResult.Success && createResult.Data is not null)
            {
                return createResult.Data.Id;
            }
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent creation — re-read the existing user
            _logger.LogDebug("User ExternalUserId collision detected, re-reading existing user");
        }

        // Re-read the existing user after collision
        var rereadResult = await _userRepository.GetByExternalUserIdAsync(userInfo.ExternalUserId, ct);
        if (rereadResult.Success && rereadResult.Data is not null)
        {
            return rereadResult.Data.Id;
        }

        throw new InvalidOperationException(
            $"Failed to resolve or create user with ExternalUserId '{userInfo.ExternalUserId}'");
    }

    /// <summary>
    /// Validates the nonce claim in the id_token against the expected nonce.
    /// </summary>
    private static bool ValidateNonce(string? idToken, string expectedNonce)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return false;
        }

        try
        {
            // Decode the JWT payload without signature verification (already validated by IDP)
            var parts = idToken.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var payload = parts[1];
            // Add Base64 padding if needed
            var remainder = payload.Length % 4;
            if (remainder == 2)
            {
                payload += "==";
            }
            else if (remainder == 3)
            {
                payload += "=";
            }

            var standardBase64 = payload.Replace('-', '+').Replace('_', '/');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(standardBase64));

            var doc = System.Text.Json.JsonDocument.Parse(json);
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
    /// Checks if an exception represents a unique constraint violation (Postgres 23505).
    /// </summary>
    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        // Walk the exception chain looking for Postgres error code 23505
        var current = ex;
        while (current is not null)
        {
            if (current.Message.Contains("23505") ||
                current.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Marker exception used to detect slug collisions within the transaction.
    /// </summary>
    private sealed class SlugCollisionException : Exception
    {
        public SlugCollisionException(string slug)
            : base($"Slug collision on '{slug}'")
        {
        }
    }
}
