using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Policies;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// The current tenant's policies (GU-96): named sets of permissions that roles grant. A thin HTTP
/// adapter: permissions, the escalation guard and the rules are enforced by
/// <see cref="IPolicyService"/>, so SDK callers get the same checks. Under <c>/api</c>, so the
/// host/token tenant check applies. Attaching policies to roles is on <c>/api/roles/{id}/policies</c>.
/// </summary>
[Route("api/policies")]
public sealed class PoliciesController : BaseController
{
    private readonly IPolicyService _policyService;

    /// <summary>
    /// Initializes a new instance of <see cref="PoliciesController"/>.
    /// </summary>
    public PoliciesController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    /// <summary>
    /// Lists the tenant's policies (by name unless sorted), each with its permission keys and how many roles use it.
    /// Accepts <c>FilterParams</c>: <c>sortBy</c> (name, roleCount, permissionCount), <c>searchTerm</c>
    /// (name, description); other fields are ignored.
    /// Needs <c>auth.permissions.read</c>. Pagination metadata is also returned in the X-Total-Count,
    /// X-Page-Number, X-Page-Size and X-Total-Pages headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<TenantPolicyDto>>>> List(
        [FromQuery] FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var result = await _policyService.ListAsync(filterParams, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Gets a policy with its permissions and the roles using it. Needs <c>auth.permissions.read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OperationResult<PolicyDetailDto>>> Get(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.GetAsync(id, cancellationToken));

    /// <summary>
    /// Creates a policy, optionally with its permission keys. Needs <c>auth.permissions.write</c>, and
    /// every key given must be held by the caller (unless TenantAdmin or SuperAdmin).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<PolicyDetailDto>>> Create(
        [FromBody] CreatePolicyDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.CreateAsync(request, cancellationToken));

    /// <summary>
    /// Renames a policy or changes its description. Needs <c>auth.permissions.write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OperationResult<PolicyDetailDto>>> Update(
        Guid id, [FromBody] UpdatePolicyDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// Replaces the policy's permissions with the given keys. Needs <c>auth.permissions.write</c>; keys
    /// being added must be held by the caller (unless TenantAdmin or SuperAdmin).
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    public async Task<ActionResult<OperationResult<PolicyDetailDto>>> SetPermissions(
        Guid id, [FromBody] SetPolicyPermissionsDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.SetPermissionsAsync(id, request, cancellationToken));

    /// <summary>
    /// Deletes a policy that no role uses. Needs <c>auth.permissions.write</c>. 409
    /// <c>POLICY_IN_USE</c> names the roles in <c>errors</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<OperationResult>> Delete(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.DeleteAsync(id, cancellationToken));
}
