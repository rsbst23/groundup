using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Token;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Default <see cref="IInvitationAcceptanceService"/>.
/// </summary>
internal sealed class InvitationAcceptanceService : IInvitationAcceptanceService
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IIdentityProviderAdminService _identityProviderAdmin;
    private readonly IAuthFlowService _authFlowService;
    private readonly HostResolvedTenant _hostResolvedTenant;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IOptions<KeycloakOptions> _keycloakOptions;
    private readonly ILogger<InvitationAcceptanceService> _logger;

    public InvitationAcceptanceService(
        IInvitationRepository invitationRepository,
        ITenantRepository tenantRepository,
        IIdentityProviderAdminService identityProviderAdmin,
        IAuthFlowService authFlowService,
        HostResolvedTenant hostResolvedTenant,
        IOptions<AuthOptions> authOptions,
        IOptions<KeycloakOptions> keycloakOptions,
        ILogger<InvitationAcceptanceService> logger)
    {
        _invitationRepository = invitationRepository;
        _tenantRepository = tenantRepository;
        _identityProviderAdmin = identityProviderAdmin;
        _authFlowService = authFlowService;
        _hostResolvedTenant = hostResolvedTenant;
        _authOptions = authOptions;
        _keycloakOptions = keycloakOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<InvitationLookupDto>> LookupAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindAsync(token, cancellationToken);
        if (invitation is null)
        {
            return OperationResult<InvitationLookupDto>.Ok(new InvitationLookupDto(false, "not_found", null, null));
        }

        if (ReasonUnusable(invitation) is { } reason)
        {
            return OperationResult<InvitationLookupDto>.Ok(new InvitationLookupDto(false, reason, null, null));
        }

        var tenant = await GetTenantAsync(invitation.TenantId, cancellationToken);
        return OperationResult<InvitationLookupDto>.Ok(new InvitationLookupDto(true, null, tenant?.Name, invitation.RoleName));
    }

    /// <inheritdoc />
    public async Task<OperationResult<FlowInitiationResult>> StartAsync(
        string token, bool forceLogin, HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var invitation = await FindAsync(token, cancellationToken);
        if (invitation is null)
        {
            return OperationResult<FlowInitiationResult>.NotFound("Invitation not found.");
        }

        if (ReasonUnusable(invitation) is { } reason)
        {
            return reason == "accepted"
                ? OperationResult<FlowInitiationResult>.Fail("This invitation has already been used.", 410, AuthErrorCodes.InvitationAlreadyUsed)
                : OperationResult<FlowInitiationResult>.Fail("This invitation is no longer valid.", 410, AuthErrorCodes.InvitationNoLongerValid);
        }

        var tenant = await GetTenantAsync(invitation.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
        {
            return OperationResult<FlowInitiationResult>.Fail("This invitation is no longer valid.", 410, AuthErrorCodes.InvitationNoLongerValid);
        }

        if (!string.IsNullOrEmpty(tenant.RealmName))
        {
            return OperationResult<FlowInitiationResult>.Fail(
                "Invitations are not available for enterprise organizations yet.", 422, AuthErrorCodes.EnterpriseNotSupported);
        }

        // On a host that names a tenant, only that tenant's invitations (Requirement 7.2)
        if (_hostResolvedTenant.Tenant is { } hostTenant && hostTenant.Id != invitation.TenantId)
        {
            return OperationResult<FlowInitiationResult>.Fail(
                "This invitation is for a different organization.", 403, AuthErrorCodes.InvitationWrongOrganization);
        }

        var page = await ChoosePageAsync(invitation.Email, forceLogin, httpContext, tenant, cancellationToken);

        return await _authFlowService.InitiateFlowAsync(
            new FlowInitiationRequest(
                FlowType.Invitation,
                ForceLogin: page == Page.ForcedSignIn,
                InvitationId: invitation.Id,
                LoginHint: invitation.Email,
                Register: page == Page.Registration),
            httpContext,
            cancellationToken);
    }

    private enum Page { SignIn, Registration, ForcedSignIn }

    /// <summary>
    /// The identity provider page for the invitee (Requirement 7.2). A session for another email gets
    /// a forced sign-in, so the existing single sign-on session cannot sign in the wrong account. A
    /// session for the invited email gets no prompt (Keycloak returns straight away). Otherwise the
    /// account look-up picks sign-in or registration; a failed look-up falls back to sign-in, which
    /// links to registration.
    /// </summary>
    private async Task<Page> ChoosePageAsync(
        string invitedEmail, bool forceLogin, HttpContext httpContext, TenantDto tenant, CancellationToken cancellationToken)
    {
        if (forceLogin)
        {
            return Page.ForcedSignIn;
        }

        var sessionEmail = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(_authOptions.Value.EmailClaimType)?.Value ?? httpContext.User.FindFirst("email")?.Value
            : null;
        if (!string.IsNullOrEmpty(sessionEmail))
        {
            return string.Equals(sessionEmail, invitedEmail, StringComparison.OrdinalIgnoreCase) ? Page.SignIn : Page.ForcedSignIn;
        }

        var realm = tenant.RealmName ?? _keycloakOptions.Value.SharedRealmName;
        try
        {
            var account = await _identityProviderAdmin.FindUserByEmailAsync(realm, invitedEmail, cancellationToken);
            if (!account.Success)
            {
                _logger.LogWarning("Invitation start: account look-up failed ({Status}); showing the sign-in page", account.StatusCode);
                return Page.SignIn;
            }

            return account.Data is null ? Page.Registration : Page.SignIn;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Invitation start: account look-up failed; showing the sign-in page");
            return Page.SignIn;
        }
    }

    private async Task<InvitationDto?> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var result = await _invitationRepository.GetByTokenHashAsync(OpaqueToken.Hash(token.Trim()), cancellationToken);
        return result.Success ? result.Data : null;
    }

    private static string? ReasonUnusable(InvitationDto invitation) => invitation.Status switch
    {
        InvitationStatus.Pending => null,
        InvitationStatus.Revoked => "revoked",
        InvitationStatus.Expired => "expired",
        _ => "accepted"
    };

    private async Task<TenantDto?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetByIdsBypassFilterAsync([tenantId], cancellationToken);
        return tenants.Success ? tenants.Data?.FirstOrDefault() : null;
    }
}
