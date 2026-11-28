using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Invitations;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// Invitation administration for the caller's current tenant (GU-68). A thin HTTP adapter: the
/// permission (<c>auth.invitations.send</c>) and the role-grant rules are enforced by
/// <see cref="IInvitationService"/>. Under <c>/api</c>, so the host/token tenant check applies. The
/// public look-up and accept endpoints are separate, under <c>/auth</c>.
/// </summary>
[Route("api/invitations")]
public sealed class InvitationsController : BaseController
{
    private readonly IInvitationService _invitationService;

    /// <summary>
    /// Initializes a new instance of <see cref="InvitationsController"/>.
    /// </summary>
    public InvitationsController(IInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    /// <summary>
    /// Invites someone to the tenant with a role. The link is emailed; the response never contains it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<InvitationDto>>> Create(
        [FromBody] CreateInvitationDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _invitationService.CreateAsync(request, cancellationToken));

    /// <summary>
    /// Lists the tenant's invitations, newest first unless sorted. Accepts <c>FilterParams</c>:
    /// <c>sortBy</c> (email, roleName, status, expiresAt, createdAt), <c>filters[status]</c>,
    /// <c>filters[roleId]</c>, <c>searchTerm</c> (email); other fields are ignored. Pagination metadata is also returned in the
    /// X-Total-Count, X-Page-Number, X-Page-Size and X-Total-Pages headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<InvitationDto>>>> List(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        var result = await _invitationService.ListAsync(filterParams, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Sends a pending or expired invitation again with a new link; the old link stops working.
    /// </summary>
    [HttpPost("{invitationId:guid}/resend")]
    public async Task<ActionResult<OperationResult<InvitationDto>>> Resend(
        Guid invitationId, CancellationToken cancellationToken = default)
        => ToActionResult(await _invitationService.ResendAsync(invitationId, cancellationToken));

    /// <summary>
    /// Revokes a pending or expired invitation and removes the membership still waiting on it.
    /// </summary>
    [HttpPost("{invitationId:guid}/revoke")]
    public async Task<ActionResult<OperationResult>> Revoke(
        Guid invitationId, CancellationToken cancellationToken = default)
        => ToActionResult(await _invitationService.RevokeAsync(invitationId, cancellationToken));
}
