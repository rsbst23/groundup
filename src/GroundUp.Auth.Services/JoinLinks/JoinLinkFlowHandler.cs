using System.Security.Claims;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Session;
using GroundUp.Core;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.JoinLinks;

/// <summary>
/// Handles the <see cref="FlowType.JoinLink"/> callback (GU-68 Requirement 9.4-9.5): checks the link
/// again, re-checks that its creator may still grant the role, then counts the use, makes the
/// membership Active (no pending step) and grants the role in one transaction, and signs the user in
/// to the link's tenant. An active member is signed in and told so; the use count and roles do not
/// change. A pending member (open invitation) becomes Active, keeps the invitation's roles, gets the
/// link's role, and the invitation closes.
/// </summary>
internal sealed class JoinLinkFlowHandler : IFlowHandler
{
    private readonly IIdentityProviderService _identityProviderService;
    private readonly IIdTokenValidator _idTokenValidator;
    private readonly CallbackSessionGuard _sessionGuard;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly IJoinLinkRepository _joinLinkRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly SignInUserResolver _userResolver;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly IRoleGrantWriter _roleGrantWriter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly ILogger<JoinLinkFlowHandler> _logger;

    public JoinLinkFlowHandler(
        IIdentityProviderService identityProviderService,
        IIdTokenValidator idTokenValidator,
        CallbackSessionGuard sessionGuard,
        IAuthFlowStateService authFlowStateService,
        IJoinLinkRepository joinLinkRepository,
        IRoleRepository roleRepository,
        IUserTenantRepository userTenantRepository,
        SignInUserResolver userResolver,
        RoleGrantPolicy rolePolicy,
        IRoleGrantWriter roleGrantWriter,
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IHttpContextAccessor httpContextAccessor,
        TenantContext tenantContext,
        TenantAccessRevoker accessRevoker,
        ILogger<JoinLinkFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _idTokenValidator = idTokenValidator;
        _sessionGuard = sessionGuard;
        _authFlowStateService = authFlowStateService;
        _joinLinkRepository = joinLinkRepository;
        _roleRepository = roleRepository;
        _userTenantRepository = userTenantRepository;
        _userResolver = userResolver;
        _rolePolicy = rolePolicy;
        _roleGrantWriter = roleGrantWriter;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _authCookieWriter = authCookieWriter;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _accessRevoker = accessRevoker;
        _logger = logger;
    }

    /// <inheritdoc />
    public FlowType HandledFlowType => FlowType.JoinLink;

    /// <inheritdoc />
    public async Task<FlowResult> HandleCallbackAsync(FlowCallbackContext context)
    {
        var state = context.ConsumedState;

        // 1. Identity provider: code exchange, ID token, session age, user info
        var tokenResponse = await _identityProviderService.ExchangeCodeForTokensAsync(
            context.AuthorizationCode, context.RedirectUri, state.Realm, codeVerifier: context.CodeVerifier);
        if (tokenResponse is null)
        {
            return await FailAsync(state.Id, "CODE_EXCHANGE_FAILED", "Failed to exchange authorization code for tokens.", 400);
        }

        var idToken = await _idTokenValidator.ValidateAsync(tokenResponse.IdToken, state.Nonce, state.Realm);
        if (!idToken.Success || idToken.Data is null)
        {
            return await FailAsync(state.Id, idToken.ErrorCode ?? IdTokenErrorCodes.Invalid, idToken.Message, 400);
        }

        var authTime = _sessionGuard.ResolveAuthTime(idToken.Data);
        if (await _sessionGuard.CheckAsync(authTime, tenantId: null) is { } systemCapReached)
        {
            return systemCapReached;
        }

        var userInfo = await _identityProviderService.GetUserInfoAsync(tokenResponse.AccessToken);
        if (userInfo is null)
        {
            return await FailAsync(state.Id, "USERINFO_FAILED", "Failed to retrieve user information from identity provider.", 400);
        }

        // 2. The link, checked again
        var link = state.JoinLinkId is { } joinLinkId
            ? (await _joinLinkRepository.GetByIdAnyTenantAsync(joinLinkId)).Data
            : null;
        if (link is null || link.Status != JoinLinkStatus.Active)
        {
            return await NoLongerValidAsync(state.Id);
        }

        var tenantId = link.TenantId;
        _tenantContext.TenantId = tenantId;

        // 3. The user (claims a pending user whose email the identity provider verified)
        var user = await _userResolver.ResolveAsync(userInfo);

        // 4. Already an active member: sign in, change nothing
        var membership = await GetMembershipAsync(user.Id, tenantId);
        if (membership is { Status: MembershipStatus.Active })
        {
            return await SignInAsync(state.Id, user.Id, tenantId, authTime, tokenResponse.IdToken, alreadyMember: true);
        }

        // 5. The creator must still be allowed to grant the role
        var role = await _roleRepository.FindByIdAnyTenantAsync(link.RoleId);
        var grantCheck = role.Success && role.Data is not null
            ? await _rolePolicy.CheckAsync(link.CreatedByUserId, user.Id, role.Data, RoleChange.Assign, RoleAssignmentSource.JoinLink)
            : OperationResult.NotFound();
        if (!grantCheck.Success)
        {
            _logger.LogInformation("Join link {JoinLinkId} refused: its creator may no longer grant role {RoleId}", link.Id, link.RoleId);
            await _authFlowStateService.MarkFailedAsync(state.Id, AuthErrorCodes.JoinLinkNoLongerValid);
            return FlowResult.Error(AuthErrorCodes.JoinLinkNoLongerValid,
                "This join link is no longer valid. Ask the organization for a new one.", 403);
        }

        // 6. The tenant's session lifetime, before anything changes
        if (await _sessionGuard.CheckAsync(authTime, tenantId) is { } tenantCapReached)
        {
            return tenantCapReached;
        }

        // 7. Count the use, make the membership Active and grant the role, together
        RoleGrantResult? grant = null;
        OperationResult transaction;
        try
        {
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // Concurrent uses can never exceed the maximum: the database checks and increments at once
                if (!await _joinLinkRepository.TryUseAsync(link.Id, ct))
                {
                    throw new LinkUnavailableException();
                }

                await ActivateMembershipAsync(user, tenantId, membership, ct);

                var granted = await _roleGrantWriter.GrantAsync(
                    user.Id, link.RoleId, tenantId, RoleAssignmentSource.JoinLink, deferEvents: true, cancellationToken: ct);
                if (!granted.Success || granted.Data is null)
                {
                    throw new InvalidOperationException($"Granting the join link's role failed: {granted.Message}");
                }

                grant = granted.Data;
            });
        }
        catch (LinkUnavailableException)
        {
            return await NoLongerValidAsync(state.Id);
        }

        if (!transaction.Success)
        {
            return await FailAsync(state.Id, "JOIN_LINK_USE_FAILED", "Joining the organization failed. Please try again.", 500);
        }

        // 8. After commit: the role counts; sign in
        await _roleGrantWriter.PublishGrantedAsync(grant!);
        _accessRevoker.EvictPermissions(user.Id, tenantId);
        _logger.LogInformation("User {UserId} joined tenant {TenantId} with join link {JoinLinkId}", user.Id, tenantId, link.Id);
        return await SignInAsync(state.Id, user.Id, tenantId, authTime, tokenResponse.IdToken, alreadyMember: false);
    }

    /// <summary>
    /// A new member gets an Active membership; a former or pending member's row becomes Active (which
    /// also closes a pending invitation).
    /// </summary>
    private async Task ActivateMembershipAsync(UserDto user, Guid tenantId, UserTenantDto? membership, CancellationToken ct)
    {
        var result = membership is null
            ? Plain(await _userTenantRepository.AddForTenantAsync(
                new UserTenantDto(Guid.Empty, user.Id, tenantId, user.ExternalUserId, MembershipStatus.Active), ct))
            : await _userTenantRepository.ReactivateMembershipAsync(user.Id, tenantId, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Activating the membership failed: {result.Message}");
        }
    }

    private static OperationResult Plain<T>(OperationResult<T> result)
        => result.Success ? OperationResult.Ok() : OperationResult.Fail(result.Message, result.StatusCode, result.ErrorCode);

    private async Task<FlowResult> SignInAsync(
        Guid stateId, Guid userId, Guid tenantId, DateTimeOffset authTime, string? idToken, bool alreadyMember)
    {
        var authTimeClaim = new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64);
        var token = await _tokenService.GenerateTokenAsync(userId, tenantId, new[] { authTimeClaim });
        if (token is null)
        {
            return await FailAsync(stateId, "TOKEN_GENERATION_FAILED", "Failed to generate authentication token.", 400);
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            await _authCookieWriter.WriteAuthCookieAsync(httpContext, token);
            if (!string.IsNullOrWhiteSpace(idToken))
            {
                await _authCookieWriter.WriteIdTokenCookieAsync(httpContext, idToken);
            }
        }

        return FlowResult.Success(token, alreadyMember: alreadyMember);
    }

    private Task<FlowResult> NoLongerValidAsync(Guid stateId)
        => FailAsync(stateId, AuthErrorCodes.JoinLinkNoLongerValid, "This join link is no longer valid.", 410);

    private async Task<FlowResult> FailAsync(Guid stateId, string errorCode, string message, int status)
    {
        await _authFlowStateService.MarkFailedAsync(stateId, errorCode);
        return FlowResult.Error(errorCode, message, status);
    }

    private async Task<UserTenantDto?> GetMembershipAsync(Guid userId, Guid tenantId)
    {
        var memberships = await _userTenantRepository.GetAllMembershipsForUserAsync(userId);
        return memberships.Success ? memberships.Data?.FirstOrDefault(m => m.TenantId == tenantId) : null;
    }

    private sealed class LinkUnavailableException : Exception;
}
