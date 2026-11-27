using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Token;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services.JoinLinks;

/// <summary>
/// Default <see cref="IJoinLinkAcceptanceService"/>.
/// </summary>
internal sealed class JoinLinkAcceptanceService : IJoinLinkAcceptanceService
{
    private readonly IJoinLinkRepository _joinLinkRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAuthFlowService _authFlowService;
    private readonly HostResolvedTenant _hostResolvedTenant;

    public JoinLinkAcceptanceService(
        IJoinLinkRepository joinLinkRepository,
        ITenantRepository tenantRepository,
        IAuthFlowService authFlowService,
        HostResolvedTenant hostResolvedTenant)
    {
        _joinLinkRepository = joinLinkRepository;
        _tenantRepository = tenantRepository;
        _authFlowService = authFlowService;
        _hostResolvedTenant = hostResolvedTenant;
    }

    /// <inheritdoc />
    public async Task<OperationResult<JoinLinkLookupDto>> LookupAsync(string token, CancellationToken cancellationToken = default)
    {
        var link = await FindAsync(token, cancellationToken);
        if (link is null)
        {
            return OperationResult<JoinLinkLookupDto>.Ok(new JoinLinkLookupDto(false, "not_found", null, null));
        }

        if (ReasonUnusable(link) is { } reason)
        {
            return OperationResult<JoinLinkLookupDto>.Ok(new JoinLinkLookupDto(false, reason, null, null));
        }

        var tenant = await GetTenantAsync(link.TenantId, cancellationToken);
        return OperationResult<JoinLinkLookupDto>.Ok(new JoinLinkLookupDto(true, null, tenant?.Name, link.RoleName));
    }

    /// <inheritdoc />
    public async Task<OperationResult<FlowInitiationResult>> StartAsync(
        string token, bool forceLogin, string? returnUrl, HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var link = await FindAsync(token, cancellationToken);
        if (link is null)
        {
            return OperationResult<FlowInitiationResult>.NotFound("Join link not found.");
        }

        var tenant = await GetTenantAsync(link.TenantId, cancellationToken);
        if (ReasonUnusable(link) is not null || tenant is null || !tenant.IsActive)
        {
            return OperationResult<FlowInitiationResult>.Fail("This join link is no longer valid.", 410, AuthErrorCodes.JoinLinkNoLongerValid);
        }

        if (!string.IsNullOrEmpty(tenant.RealmName))
        {
            return OperationResult<FlowInitiationResult>.Fail(
                "Join links are not available for enterprise organizations yet.", 422, AuthErrorCodes.EnterpriseNotSupported);
        }

        if (_hostResolvedTenant.Tenant is { } hostTenant && hostTenant.Id != link.TenantId)
        {
            return OperationResult<FlowInitiationResult>.Fail(
                "This join link is for a different organization.", 403, AuthErrorCodes.InvitationWrongOrganization);
        }

        return await _authFlowService.InitiateFlowAsync(
            new FlowInitiationRequest(FlowType.JoinLink, ReturnUrl: returnUrl, ForceLogin: forceLogin, JoinLinkId: link.Id),
            httpContext,
            cancellationToken);
    }

    private async Task<JoinLinkDto?> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var result = await _joinLinkRepository.GetByTokenHashAsync(OpaqueToken.Hash(token.Trim()), cancellationToken);
        return result.Success ? result.Data : null;
    }

    private static string? ReasonUnusable(JoinLinkDto link) => link.Status switch
    {
        JoinLinkStatus.Active => null,
        JoinLinkStatus.Revoked => "revoked",
        JoinLinkStatus.Expired => "expired",
        _ => "used_up"
    };

    private async Task<TenantDto?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetByIdsBypassFilterAsync([tenantId], cancellationToken);
        return tenants.Success ? tenants.Data?.FirstOrDefault() : null;
    }
}
