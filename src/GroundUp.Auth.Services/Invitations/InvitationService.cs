using FluentValidation;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Auth.Services.Token;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Default <see cref="IInvitationService"/>. The permission is checked by the authorization proxy;
/// this class checks the role-grant rules, the tenant kind and the membership state, and writes the
/// pending user, membership, role and invitation in one transaction.
/// </summary>
internal sealed class InvitationService : IInvitationService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IRoleGrantWriter _roleGrantWriter;
    private readonly RoleGrantPolicy _rolePolicy;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingsService _settingsService;
    private readonly IInvitationEmailSender _emailSender;
    private readonly IValidator<CreateInvitationDto> _validator;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        ITenantRepository tenantRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IUserTenantRepository userTenantRepository,
        IInvitationRepository invitationRepository,
        IRoleGrantWriter roleGrantWriter,
        RoleGrantPolicy rolePolicy,
        TenantAccessRevoker accessRevoker,
        IUnitOfWork unitOfWork,
        ISettingsService settingsService,
        IInvitationEmailSender emailSender,
        IValidator<CreateInvitationDto> validator,
        ILogger<InvitationService> logger)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _tenantRepository = tenantRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _userTenantRepository = userTenantRepository;
        _invitationRepository = invitationRepository;
        _roleGrantWriter = roleGrantWriter;
        _rolePolicy = rolePolicy;
        _accessRevoker = accessRevoker;
        _unitOfWork = unitOfWork;
        _settingsService = settingsService;
        _emailSender = emailSender;
        _validator = validator;
        _logger = logger;
    }

    // ── Create ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<InvitationDto>> CreateAsync(
        CreateInvitationDto request, CancellationToken cancellationToken = default)
    {
        var tenantResult = await GetCurrentStandardTenantAsync(cancellationToken);
        if (!tenantResult.Success)
        {
            return Fail<InvitationDto, TenantDto>(tenantResult);
        }

        var tenant = tenantResult.Data!;

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<InvitationDto>.BadRequest(
                validation.Errors[0].ErrorMessage, validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var roleResult = await GetGrantableRoleAsync(request.RoleId, tenant.Id, cancellationToken);
        if (!roleResult.Success)
        {
            return Fail<InvitationDto, RoleDto>(roleResult);
        }

        var role = roleResult.Data!;

        var acceptUrl = await GetAcceptUrlTemplateAsync(cancellationToken);
        if (acceptUrl is null)
        {
            return UrlNotConfigured<InvitationDto>();
        }

        var email = request.Email!.Trim();
        var normalizedEmail = Normalize(email);
        var expiresAt = await ComputeExpiryAsync(request.ExpiresAt, cancellationToken);
        var token = OpaqueToken.Create();
        var callerId = _currentUser.UserId;

        InvitationDto? invitation = null;
        RoleGrantResult? grant = null;
        OperationResult transaction;
        try
        {
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var userId = await FindOrCreateInviteeAsync(email, tenant.Id, ct);
                await EnsurePendingMembershipAsync(userId, tenant.Id, ct);

                var granted = await _roleGrantWriter.GrantAsync(
                    userId, role.Id, tenant.Id, RoleAssignmentSource.Invitation, deferEvents: true, cancellationToken: ct);
                if (!granted.Success || granted.Data is null)
                {
                    throw new AbortException(OperationResult.Fail(granted.Message, granted.StatusCode, granted.ErrorCode));
                }

                grant = granted.Data;

                var added = await _invitationRepository.AddAsync(
                    new InvitationDto(Guid.Empty, tenant.Id, email, role.Id, role.Name, userId, callerId, expiresAt,
                        InvitationStatus.Pending, null, null, null, default),
                    normalizedEmail, OpaqueToken.Hash(token), ct);
                if (!added.Success || added.Data is null)
                {
                    throw new AbortException(added.StatusCode == 409
                        ? InvitationPending()
                        : OperationResult.Fail(added.Message, added.StatusCode, added.ErrorCode));
                }

                invitation = added.Data;
            }, cancellationToken);
        }
        catch (AbortException abort)
        {
            return Fail<InvitationDto>(abort.Result);
        }

        if (!transaction.Success)
        {
            // A unique violation at commit: a concurrent invitation for the same email won
            return transaction.StatusCode == 409 ? Fail<InvitationDto>(InvitationPending()) : Fail<InvitationDto>(transaction);
        }

        await _roleGrantWriter.PublishGrantedAsync(grant!, cancellationToken);
        await SendAsync(invitation!, tenant, token, acceptUrl, cancellationToken);

        _logger.LogInformation(
            "Invitation {InvitationId} created in tenant {TenantId} by {CallerId}", invitation!.Id, tenant.Id, callerId);
        return OperationResult<InvitationDto>.Ok(invitation, statusCode: 201);
    }

    /// <summary>
    /// The invitee (GU-68 Requirement 6.3): when exactly one user has the email, that user; else an
    /// unlinked pending user with the email; else a new pending user. Refuses when any user with the
    /// email is already an Active member.
    /// </summary>
    private async Task<Guid> FindOrCreateInviteeAsync(string email, Guid tenantId, CancellationToken ct)
    {
        var candidates = (await _userRepository.GetAllByEmailAsync(email, ct)).Data ?? [];

        foreach (var candidate in candidates)
        {
            var membership = await GetMembershipAsync(candidate.Id, tenantId, ct);
            if (membership is { Status: MembershipStatus.Active })
            {
                throw new AbortException(OperationResult.Fail(
                    "This person is already a member of the organization.", 409, AuthErrorCodes.AlreadyMember));
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0].Id;
        }

        var pending = candidates.FirstOrDefault(c => c.ExternalUserId is null);
        if (pending is not null)
        {
            return pending.Id;
        }

        var created = await _userRepository.AddAsync(
            new UserDto(Guid.Empty, null, email, email, IsActive: true), ct);
        if (!created.Success || created.Data is null)
        {
            throw new AbortException(OperationResult.Fail(created.Message, created.StatusCode, created.ErrorCode));
        }

        return created.Data.Id;
    }

    private async Task EnsurePendingMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var membership = await GetMembershipAsync(userId, tenantId, ct);
        switch (membership?.Status)
        {
            case MembershipStatus.Active:
                throw new AbortException(OperationResult.Fail(
                    "This person is already a member of the organization.", 409, AuthErrorCodes.AlreadyMember));
            case MembershipStatus.Pending:
                throw new AbortException(InvitationPending());
            case MembershipStatus.Inactive:
                var reopened = await _userTenantRepository.MarkPendingAsync(userId, tenantId, ct);
                if (!reopened.Success)
                {
                    throw new AbortException(reopened);
                }

                break;
            default:
                var added = await _userTenantRepository.AddForTenantAsync(
                    new UserTenantDto(Guid.Empty, userId, tenantId, null, MembershipStatus.Pending), ct);
                if (!added.Success)
                {
                    throw new AbortException(added.StatusCode == 409
                        ? InvitationPending()
                        : OperationResult.Fail(added.Message, added.StatusCode, added.ErrorCode));
                }

                break;
        }
    }

    // ── List, resend, revoke ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<InvitationDto>>> ListAsync(
        FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<PaginatedData<InvitationDto>>.Fail(
                "A tenant must be selected to list invitations.", 403, ErrorCodes.TenantRequired);
        }

        return await _invitationRepository.ListAsync(tenantId, filterParams, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<InvitationDto>> ResendAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var tenantResult = await GetCurrentStandardTenantAsync(cancellationToken);
        if (!tenantResult.Success)
        {
            return Fail<InvitationDto, TenantDto>(tenantResult);
        }

        var tenant = tenantResult.Data!;
        var existing = await _invitationRepository.GetByIdAsync(tenant.Id, invitationId, cancellationToken);
        if (!existing.Success || existing.Data is null)
        {
            return OperationResult<InvitationDto>.NotFound("Invitation not found.");
        }

        if (existing.Data.Status is not (InvitationStatus.Pending or InvitationStatus.Expired))
        {
            return OperationResult<InvitationDto>.Fail("Only a pending or expired invitation can be resent.", 409, ErrorCodes.Conflict);
        }

        // Resending re-issues the grant, so the resender must be allowed to grant the role
        var roleResult = await GetGrantableRoleAsync(existing.Data.RoleId, tenant.Id, cancellationToken);
        if (!roleResult.Success)
        {
            return Fail<InvitationDto, RoleDto>(roleResult);
        }

        var acceptUrl = await GetAcceptUrlTemplateAsync(cancellationToken);
        if (acceptUrl is null)
        {
            return UrlNotConfigured<InvitationDto>();
        }

        var token = OpaqueToken.Create();
        var expiresAt = await ComputeExpiryAsync(null, cancellationToken);
        var reissued = await _invitationRepository.ReissueAsync(
            tenant.Id, invitationId, OpaqueToken.Hash(token), expiresAt, _currentUser.UserId, cancellationToken);
        if (!reissued.Success)
        {
            return Fail<InvitationDto>(reissued);
        }

        var invitation = (await _invitationRepository.GetByIdAsync(tenant.Id, invitationId, cancellationToken)).Data!;
        await SendAsync(invitation, tenant, token, acceptUrl, cancellationToken);

        _logger.LogInformation("Invitation {InvitationId} resent by {CallerId}", invitationId, _currentUser.UserId);
        return OperationResult<InvitationDto>.Ok(invitation);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult.Fail("A tenant must be selected to revoke invitations.", 403, ErrorCodes.TenantRequired);
        }

        var existing = await _invitationRepository.GetByIdAsync(tenantId, invitationId, cancellationToken);
        if (!existing.Success || existing.Data is null)
        {
            return OperationResult.NotFound("Invitation not found.");
        }

        var invitation = existing.Data;
        List<UserRoleDto> removedRoles = [];
        OperationResult transaction;
        try
        {
            transaction = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var revoked = await _invitationRepository.RevokeAsync(tenantId, invitationId, _currentUser.UserId, ct);
                if (!revoked.Success)
                {
                    throw new AbortException(revoked);
                }

                // A membership still waiting on this invitation goes, with every role it was given.
                // A member who became Active another way keeps their access.
                var membership = await GetMembershipAsync(invitation.UserId, tenantId, ct);
                if (membership is { Status: MembershipStatus.Pending })
                {
                    var deactivated = await _userTenantRepository.DeactivateMembershipAsync(invitation.UserId, tenantId, ct);
                    if (!deactivated.Success)
                    {
                        throw new AbortException(OperationResult.Fail(deactivated.Message, deactivated.StatusCode, deactivated.ErrorCode));
                    }

                    removedRoles = deactivated.Data ?? [];
                }
            }, cancellationToken);
        }
        catch (AbortException abort)
        {
            return abort.Result;
        }

        if (!transaction.Success)
        {
            return transaction;
        }

        if (removedRoles.Count > 0)
        {
            await _accessRevoker.EndAccessAsync(invitation.UserId, tenantId, removedRoles, cancellationToken);
        }

        await _accessRevoker.PublishSafelyAsync(
            new InvitationRevoked { InvitationId = invitationId, InvitedUserId = invitation.UserId, TenantId = tenantId, UserId = _currentUser.UserId },
            cancellationToken);

        _logger.LogInformation("Invitation {InvitationId} revoked by {CallerId}", invitationId, _currentUser.UserId);
        return OperationResult.Ok();
    }

    // ── Shared steps ──────────────────────────────────────────────

    /// <summary>The current tenant, refused for enterprise tenants until GU-69.</summary>
    private async Task<OperationResult<TenantDto>> GetCurrentStandardTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return OperationResult<TenantDto>.Fail("A tenant must be selected to manage invitations.", 403, ErrorCodes.TenantRequired);
        }

        var tenants = await _tenantRepository.GetByIdsBypassFilterAsync([tenantId], cancellationToken);
        var tenant = tenants.Success ? tenants.Data?.FirstOrDefault() : null;
        if (tenant is null)
        {
            return OperationResult<TenantDto>.NotFound("Organization not found.");
        }

        if (!string.IsNullOrEmpty(tenant.RealmName))
        {
            return OperationResult<TenantDto>.Fail(
                "Invitations are not available for enterprise organizations yet.", 422, AuthErrorCodes.EnterpriseNotSupported);
        }

        return OperationResult<TenantDto>.Ok(tenant);
    }

    /// <summary>A role of the tenant that the caller may grant.</summary>
    private async Task<OperationResult<RoleDto>> GetGrantableRoleAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.FindByIdAnyTenantAsync(roleId, cancellationToken);
        if (!role.Success || role.Data is null || role.Data.TenantId != tenantId)
        {
            return OperationResult<RoleDto>.NotFound("Role not found.");
        }

        var check = await _rolePolicy.CheckAsync(
            _currentUser.UserId, Guid.Empty, role.Data, RoleChange.Assign, RoleAssignmentSource.Invitation, cancellationToken);
        return check.Success ? OperationResult<RoleDto>.Ok(role.Data) : Fail<RoleDto>(check);
    }

    private async Task<UserTenantDto?> GetMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var memberships = await _userTenantRepository.GetAllMembershipsForUserAsync(userId, ct);
        return memberships.Success ? memberships.Data?.FirstOrDefault(m => m.TenantId == tenantId) : null;
    }

    private async Task<string?> GetAcceptUrlTemplateAsync(CancellationToken cancellationToken)
    {
        var result = await _settingsService.GetAsync<string>(InvitationSettingKeys.AcceptUrl, cancellationToken);
        var template = result is { Success: true } ? result.Data?.Trim() : null;
        return string.IsNullOrEmpty(template) || !template.Contains(InvitationSettingKeys.TokenPlaceholder, StringComparison.Ordinal)
            ? null
            : template;
    }

    /// <summary>
    /// Now plus the lifetime setting (clamped to its limits), or the requested expiry when that is earlier.
    /// </summary>
    private async Task<DateTime> ComputeExpiryAsync(DateTime? requested, CancellationToken cancellationToken)
    {
        var result = await _settingsService.GetAsync<int>(InvitationSettingKeys.LifetimeHours, cancellationToken);
        var hours = result is { Success: true } && result.Data > 0 ? result.Data : InvitationSettingKeys.LifetimeHoursDefault;
        hours = Math.Clamp(hours, InvitationSettingKeys.LifetimeHoursMin, InvitationSettingKeys.LifetimeHoursMax);

        var latest = DateTime.UtcNow.AddHours(hours);
        return requested is { } value && value.ToUniversalTime() < latest ? value.ToUniversalTime() : latest;
    }

    /// <summary>
    /// Builds the link and hands it to the email sender. A send failure is logged; the invitation
    /// stays and an administrator can resend it.
    /// </summary>
    private async Task SendAsync(InvitationDto invitation, TenantDto tenant, string token, string template, CancellationToken cancellationToken)
    {
        var acceptUrl = template
            .Replace(InvitationSettingKeys.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal)
            .Replace(InvitationSettingKeys.TenantSlugPlaceholder, Uri.EscapeDataString(tenant.Slug), StringComparison.Ordinal);

        try
        {
            await _emailSender.SendAsync(new InvitationEmail(
                invitation.Email, tenant.Name, invitation.RoleName ?? string.Empty, acceptUrl, invitation.ExpiresAt, invitation.Id),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Invitation {InvitationId} was saved but its email could not be sent", invitation.Id);
        }
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static OperationResult InvitationPending() => OperationResult.Fail(
        "This email already has a pending invitation to the organization. Resend or revoke it instead.",
        409, AuthErrorCodes.InvitationPending);

    private static OperationResult<T> UrlNotConfigured<T>() => OperationResult<T>.Fail(
        $"Invitations cannot be sent until the {InvitationSettingKeys.AcceptUrl} setting is configured with a {InvitationSettingKeys.TokenPlaceholder} placeholder.",
        422, AuthErrorCodes.InvitationUrlNotConfigured);

    private static OperationResult<T> Fail<T>(OperationResult source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    private static OperationResult<T> Fail<T, TSource>(OperationResult<TSource> source)
        => OperationResult<T>.Fail(source.Message, source.StatusCode, source.ErrorCode, source.Errors);

    /// <summary>Stops the transaction with a business result; it rolls back.</summary>
    private sealed class AbortException(OperationResult result) : Exception(result.Message)
    {
        public OperationResult Result { get; } = result;
    }
}
