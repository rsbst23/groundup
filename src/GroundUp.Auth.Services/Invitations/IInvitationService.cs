using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// Invitations to the current tenant, for administrators (GU-68 Requirement 6). Registered by
/// <c>AddGroundUpAuth</c> through the authorization proxy; every method needs
/// <c>auth.invitations.send</c>, and creating or resending also needs the right to grant the
/// invitation's role. No method returns a token: it only goes into the invitation email.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Invites <see cref="CreateInvitationDto.Email"/>: creates (or reuses) the user, a Pending
    /// membership with the role already granted, and the invitation, then emails the link.
    /// </summary>
    /// <returns>
    /// 201 with the invitation; 400 for invalid input; 403 when the caller may not grant the role;
    /// 404 for an unknown role; 409 <c>ALREADY_MEMBER</c> or <c>INVITATION_PENDING</c>;
    /// 422 <c>ENTERPRISE_NOT_SUPPORTED</c> or <c>INVITATION_URL_NOT_CONFIGURED</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<InvitationDto>> CreateAsync(CreateInvitationDto request, CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's invitations, newest first unless sorted. Sort: <c>email</c>, <c>roleName</c>, <c>status</c>, <c>expiresAt</c>, <c>createdAt</c>; filter: <c>status</c> (as shown, so Expired finds Pending invitations past their expiry), <c>roleId</c>; search: email. Other fields are ignored.</summary>
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<PaginatedData<InvitationDto>>> ListAsync(FilterParams filterParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a pending (or expired) invitation again with a new token and a fresh expiry; the old
    /// link stops working. The caller becomes the invitation's creator for the acceptance re-check.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult<InvitationDto>> ResendAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a pending (or expired) invitation. A membership that is still Pending is deactivated
    /// and its roles removed; the pending user record is kept.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.InvitationsSend)]
    Task<OperationResult> RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default);
}
