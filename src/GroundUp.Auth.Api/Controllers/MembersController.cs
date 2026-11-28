using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// Member administration for the caller's current tenant (GU-68): list members, remove a member,
/// grant and remove roles. A thin HTTP adapter: the permissions and role rules are enforced by
/// <see cref="IMemberManagementService"/> and <see cref="IUserRoleService"/>, so SDK callers get the
/// same checks. Under <c>/api</c>, so the host/token tenant check applies.
/// </summary>
[Route("api/members")]
public sealed class MembersController : BaseController
{
    private readonly IMemberManagementService _memberManagementService;
    private readonly IUserRoleService _userRoleService;

    /// <summary>
    /// Initializes a new instance of <see cref="MembersController"/>.
    /// </summary>
    public MembersController(IMemberManagementService memberManagementService, IUserRoleService userRoleService)
    {
        _memberManagementService = memberManagementService;
        _userRoleService = userRoleService;
    }

    /// <summary>
    /// Lists the tenant's Active and Pending members with their roles. Needs <c>auth.members.read</c>.
    /// Accepts <c>FilterParams</c>: <c>sortBy</c> (email, displayName, status), <c>filters[status]</c>,
    /// <c>searchTerm</c> (email, display name); other fields are ignored.
    /// Pagination metadata is also returned in the X-Total-Count, X-Page-Number, X-Page-Size and
    /// X-Total-Pages headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<TenantMemberDto>>>> List(
        [FromQuery] FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var result = await _memberManagementService.ListMembersAsync(filterParams, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Removes a member from the tenant. Needs <c>auth.members.manage</c>, and the right to remove
    /// each role the member holds.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    public async Task<ActionResult<OperationResult>> Remove(Guid userId, CancellationToken cancellationToken = default)
        => ToActionResult(await _memberManagementService.RemoveMemberAsync(userId, cancellationToken));

    /// <summary>
    /// Grants a role to a member (source Manual).
    /// </summary>
    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<OperationResult<UserRoleDto>>> AssignRole(
        Guid userId, Guid roleId, CancellationToken cancellationToken = default)
        => ToActionResult(await _userRoleService.AssignRoleAsync(userId, roleId, cancellationToken));

    /// <summary>
    /// Removes a role from a member, whatever the source it was granted from.
    /// </summary>
    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<OperationResult>> RemoveRole(
        Guid userId, Guid roleId, CancellationToken cancellationToken = default)
        => ToActionResult(await _userRoleService.RemoveRoleAsync(userId, roleId, cancellationToken));
}
