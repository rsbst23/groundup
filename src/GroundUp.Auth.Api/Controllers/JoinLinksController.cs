using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.JoinLinks;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// Join-link administration for the caller's current tenant (GU-68). A thin HTTP adapter: the
/// permission (<c>auth.joinlinks.manage</c>) and the role-grant rules are enforced by
/// <see cref="IJoinLinkService"/>. Under <c>/api</c>, so the host/token tenant check applies. The
/// public look-up and join endpoints are under <c>/auth/join-links</c>.
/// </summary>
[Route("api/join-links")]
public sealed class JoinLinksController : BaseController
{
    private readonly IJoinLinkService _joinLinkService;

    /// <summary>
    /// Initializes a new instance of <see cref="JoinLinksController"/>.
    /// </summary>
    public JoinLinksController(IJoinLinkService joinLinkService)
    {
        _joinLinkService = joinLinkService;
    }

    /// <summary>Creates a join link. The response is the only time its token is shown.</summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<CreatedJoinLinkDto>>> Create(
        [FromBody] CreateJoinLinkDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _joinLinkService.CreateAsync(request, cancellationToken));

    /// <summary>
    /// Lists the tenant's join links, newest first. Pagination metadata is also returned in the
    /// X-Total-Count, X-Page-Number, X-Page-Size and X-Total-Pages headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<JoinLinkDto>>>> List(
        [FromQuery] PaginationParams paging, CancellationToken cancellationToken = default)
    {
        var result = await _joinLinkService.ListAsync(paging, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>Replaces the link's token; the old link stops working. Returns the new token once.</summary>
    [HttpPost("{joinLinkId:guid}/regenerate")]
    public async Task<ActionResult<OperationResult<CreatedJoinLinkDto>>> Regenerate(
        Guid joinLinkId, CancellationToken cancellationToken = default)
        => ToActionResult(await _joinLinkService.RegenerateAsync(joinLinkId, cancellationToken));

    /// <summary>Revokes the link. People who already joined keep their membership.</summary>
    [HttpPost("{joinLinkId:guid}/revoke")]
    public async Task<ActionResult<OperationResult>> Revoke(Guid joinLinkId, CancellationToken cancellationToken = default)
        => ToActionResult(await _joinLinkService.RevokeAsync(joinLinkId, cancellationToken));
}
