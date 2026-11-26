using System.Security.Claims;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Identity;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Session;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Handles the <see cref="FlowType.Invitation"/> callback (GU-68 Requirement 7.3-7.6): checks the
/// invitation again (it may have changed during sign-in), matches the signed-in email, claims or
/// merges the pending user, re-checks that the creator may still grant the role, then activates the
/// membership and signs the user in to the invitation's tenant. A refusal leaves the invitation as
/// it was, so the invitee can start again from the link.
/// </summary>
internal sealed class InvitationFlowHandler : IFlowHandler
{
    private const string EmailVerifiedAttribute = "email_verified";

    private readonly IIdentityProviderService _identityProviderService;
    private readonly IIdTokenValidator _idTokenValidator;
    private readonly CallbackSessionGuard _sessionGuard;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly SignInUserResolver _userResolver;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingsService _settingsService;
    private readonly ITokenService _tokenService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantContext _tenantContext;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly ILogger<InvitationFlowHandler> _logger;

    public InvitationFlowHandler(
        IIdentityProviderService identityProviderService,
        IIdTokenValidator idTokenValidator,
        CallbackSessionGuard sessionGuard,
        IAuthFlowStateService authFlowStateService,
        IInvitationRepository invitationRepository,
        IRoleRepository roleRepository,
        IUserTenantRepository userTenantRepository,
        SignInUserResolver userResolver,
        RoleGrantPolicy rolePolicy,
        IUnitOfWork unitOfWork,
        ISettingsService settingsService,
        ITokenService tokenService,
        IAuthCookieWriter authCookieWriter,
        IHttpContextAccessor httpContextAccessor,
        TenantContext tenantContext,
        TenantAccessRevoker accessRevoker,
        ILogger<InvitationFlowHandler> logger)
    {
        _identityProviderService = identityProviderService;
        _idTokenValidator = idTokenValidator;
        _sessionGuard = sessionGuard;
        _authFlowStateService = authFlowStateService;
        _invitationRepository = invitationRepository;
        _roleRepository = roleRepository;
        _userTenantRepository = userTenantRepository;
        _userResolver = userResolver;
        _rolePolicy = rolePolicy;
        _unitOfWork = unitOfWork;
        _settingsService = settingsService;
        _tokenService = tokenService;
        _authCookieWriter = authCookieWriter;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _accessRevoker = accessRevoker;
        _logger = logger;
    }

    /// <inheritdoc />
    public FlowType HandledFlowType => FlowType.Invitation;

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

        // 2. The invitation, checked again
        var invitation = state.InvitationId is { } invitationId
            ? (await _invitationRepository.GetByIdAnyTenantAsync(invitationId)).Data
            : null;
        if (invitation is null || invitation.Status is InvitationStatus.Revoked or InvitationStatus.Expired)
        {
            return await FailAsync(state.Id, AuthErrorCodes.InvitationNoLongerValid, "This invitation is no longer valid.", 410);
        }

        if (invitation.Status == InvitationStatus.Accepted)
        {
            return await FailAsync(state.Id, AuthErrorCodes.InvitationAlreadyUsed, "This invitation has already been used.", 410);
        }

        // 3. The signed-in email must be the invited one; the link counts as proof of it unless the
        //    application requires the identity provider's verification as well
        if (!string.Equals(userInfo.Email?.Trim(), invitation.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(state.Id, AuthErrorCodes.InvitationEmailMismatch,
                $"This invitation was sent to {invitation.Email}. Sign in with that email address to accept it.", 403);
        }

        if (await RequiresVerifiedEmailAsync() && !IsEmailVerified(userInfo))
        {
            return await FailAsync(state.Id, AuthErrorCodes.EmailNotVerified,
                "Verify your email address with the identity provider, then open the invitation link again.", 403);
        }

        // 4. Act in the invitation's tenant from here on (role checks, token)
        var tenantId = invitation.TenantId;
        _tenantContext.TenantId = tenantId;

        // 5. Claim the pending user, or merge it into the signed-in existing user
        var user = await _userResolver.ResolveAsync(userInfo, invitation.UserId);

        // 6. Already an active member: sign in, close the invitation, change no roles
        var membership = await GetMembershipAsync(user.Id, tenantId);
        if (membership is { Status: MembershipStatus.Active })
        {
            await _invitationRepository.TryAcceptAsync(invitation.Id, user.Id);
            await PublishAcceptedAsync(invitation.Id, tenantId, user.Id, alreadyMember: true);
            return await SignInAsync(state.Id, user.Id, tenantId, authTime, tokenResponse.IdToken, alreadyMember: true);
        }

        // 7. The creator must still be allowed to grant the role
        var role = await _roleRepository.FindByIdAnyTenantAsync(invitation.RoleId);
        var grantCheck = role.Success && role.Data is not null
            ? await _rolePolicy.CheckAsync(invitation.CreatedByUserId, user.Id, role.Data, RoleChange.Assign, RoleAssignmentSource.Invitation)
            : OperationResult.NotFound();
        if (!grantCheck.Success)
        {
            _logger.LogInformation(
                "Invitation {InvitationId} refused: its creator may no longer grant role {RoleId}", invitation.Id, invitation.RoleId);
            return await FailAsync(state.Id, AuthErrorCodes.InvitationNoLongerValid,
                "This invitation is no longer valid. Ask the organization to send a new one.", 403);
        }

        // 8. The tenant's session lifetime, before anything changes
        if (await _sessionGuard.CheckAsync(authTime, tenantId) is { } tenantCapReached)
        {
            return tenantCapReached;
        }

        // 9. Accept and activate, together
        if (membership is null)
        {
            return await FailAsync(state.Id, AuthErrorCodes.InvitationNoLongerValid, "This invitation is no longer valid.", 410);
        }

        OperationResult transaction;
        try
        {
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // Of two concurrent acceptances only one gets past this (Requirement 7.4)
                if (!await _invitationRepository.TryAcceptAsync(invitation.Id, user.Id, ct))
                {
                    throw new InvitationAlreadyUsedException();
                }

                var activated = await _userTenantRepository.ReactivateMembershipAsync(user.Id, tenantId, ct);
                if (!activated.Success)
                {
                    throw new InvalidOperationException($"Activating the membership failed: {activated.Message}");
                }
            });
        }
        catch (InvitationAlreadyUsedException)
        {
            return await FailAsync(state.Id, AuthErrorCodes.InvitationAlreadyUsed, "This invitation has already been used.", 410);
        }

        if (!transaction.Success)
        {
            return await FailAsync(state.Id, "INVITATION_ACCEPT_FAILED", "The invitation could not be accepted. Please try again.", 500);
        }

        // 10. After commit: the roles now count; sign in
        _accessRevoker.EvictPermissions(user.Id, tenantId);
        await PublishAcceptedAsync(invitation.Id, tenantId, user.Id, alreadyMember: false);
        _logger.LogInformation("Invitation {InvitationId} accepted by user {UserId}", invitation.Id, user.Id);
        return await SignInAsync(state.Id, user.Id, tenantId, authTime, tokenResponse.IdToken, alreadyMember: false);
    }

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

    private async Task<FlowResult> FailAsync(Guid stateId, string errorCode, string message, int status)
    {
        await _authFlowStateService.MarkFailedAsync(stateId, errorCode);
        return FlowResult.Error(errorCode, message, status);
    }

    private Task PublishAcceptedAsync(Guid invitationId, Guid tenantId, Guid userId, bool alreadyMember)
        => _accessRevoker.PublishSafelyAsync(
            new InvitationAccepted { InvitationId = invitationId, AlreadyMember = alreadyMember, TenantId = tenantId, UserId = userId },
            CancellationToken.None);

    private async Task<UserTenantDto?> GetMembershipAsync(Guid userId, Guid tenantId)
    {
        var memberships = await _userTenantRepository.GetAllMembershipsForUserAsync(userId);
        return memberships.Success ? memberships.Data?.FirstOrDefault(m => m.TenantId == tenantId) : null;
    }

    private async Task<bool> RequiresVerifiedEmailAsync()
    {
        try
        {
            var result = await _settingsService.GetAsync<bool>(InvitationSettingKeys.RequireVerifiedEmail);
            return result is { Success: true, Data: true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed: when the rule cannot be read, require verification
            _logger.LogWarning(ex, "The {Setting} setting could not be read; requiring a verified email", InvitationSettingKeys.RequireVerifiedEmail);
            return true;
        }
    }

    private static bool IsEmailVerified(ExternalUserInfo userInfo)
        => userInfo.Attributes is not null
           && userInfo.Attributes.TryGetValue(EmailVerifiedAttribute, out var value)
           && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private sealed class InvitationAlreadyUsedException : Exception;
}
