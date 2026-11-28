using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// Role administration for the caller's current tenant (GU-95): list, create, rename and delete
/// roles, and set the tenant's default role for new members. A thin HTTP adapter: permissions and
/// rules are enforced by <see cref="IRoleService"/>, so SDK callers get the same checks. Under
/// <c>/api</c>, so the host/token tenant check applies.
/// </summary>
[Route("api/roles")]
public sealed class RolesController : BaseController
{
    private readonly IRoleService _roleService;

    /// <summary>
    /// Initializes a new instance of <see cref="RolesController"/>.
    /// </summary>
    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Lists the tenant's roles, each with <c>isDefault</c>. Needs <c>auth.roles.read</c>. Pagination
    /// metadata is also returned in the X-Total-Count, X-Page-Number, X-Page-Size and X-Total-Pages headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<TenantRoleDto>>>> List(
        [FromQuery] PaginationParams paging,
        CancellationToken cancellationToken = default)
    {
        var result = await _roleService.ListAsync(paging, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Gets a role. Needs <c>auth.roles.read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OperationResult<TenantRoleDto>>> Get(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.GetAsync(id, cancellationToken));

    /// <summary>
    /// Creates a role (type Application or Workspace). Needs <c>auth.roles.write</c>.
    /// 409 <c>ROLE_NAME_TAKEN</c> when the name is used, ignoring case.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<TenantRoleDto>>> Create(
        [FromBody] CreateRoleDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.CreateAsync(request, cancellationToken));

    /// <summary>
    /// Renames a role or changes its description. Needs <c>auth.roles.write</c>. A built-in role keeps
    /// its name (409 <c>ROLE_IS_BUILT_IN</c>).
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OperationResult<TenantRoleDto>>> Update(
        Guid id, [FromBody] UpdateRoleDto request, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// Deletes an unused role. Needs <c>auth.roles.delete</c>. 409 <c>ROLE_IN_USE</c> lists what uses it
    /// in <c>errors</c>; 409 <c>ROLE_IS_BUILT_IN</c> for TenantAdmin.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<OperationResult>> Delete(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.DeleteAsync(id, cancellationToken));

    /// <summary>
    /// Gets the role new members receive; <c>data</c> is null when there is none. Needs <c>auth.roles.read</c>.
    /// </summary>
    [HttpGet("default")]
    public async Task<ActionResult<OperationResult<TenantRoleDto?>>> GetDefault(CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.GetDefaultAsync(cancellationToken));

    /// <summary>
    /// Makes a role the one new members receive. Needs <c>auth.roles.write</c> and the right to grant
    /// the role; a built-in role is refused (409 <c>ROLE_IS_BUILT_IN</c>).
    /// </summary>
    [HttpPut("{id:guid}/default")]
    public async Task<ActionResult<OperationResult<TenantRoleDto>>> SetDefault(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.SetDefaultAsync(id, cancellationToken));

    /// <summary>
    /// Clears the default role: new members then receive no role. Needs <c>auth.roles.write</c>.
    /// </summary>
    [HttpDelete("default")]
    public async Task<ActionResult<OperationResult>> ClearDefault(CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.ClearDefaultAsync(cancellationToken));

    /// <summary>
    /// The policies attached to a role (GU-96). Needs <c>auth.roles.read</c>. TenantAdmin has none.
    /// </summary>
    [HttpGet("{id:guid}/policies")]
    public async Task<ActionResult<OperationResult<List<PolicyDto>>>> GetPolicies(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.GetPoliciesAsync(id, cancellationToken));

    /// <summary>
    /// Attaches a policy to a role (GU-96): 201, or 200 when already attached. Needs
    /// <c>auth.roles.write</c> and every permission of the policy (unless TenantAdmin or SuperAdmin).
    /// Members holding the role get the permissions on their next request.
    /// </summary>
    [HttpPost("{id:guid}/policies/{policyId:guid}")]
    public async Task<ActionResult<OperationResult<RolePolicyDto>>> AttachPolicy(
        Guid id, Guid policyId, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.AttachPolicyAsync(id, policyId, cancellationToken));

    /// <summary>
    /// Detaches a policy from a role (GU-96). Needs <c>auth.roles.write</c>. 404 when not attached.
    /// </summary>
    [HttpDelete("{id:guid}/policies/{policyId:guid}")]
    public async Task<ActionResult<OperationResult>> DetachPolicy(
        Guid id, Guid policyId, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.DetachPolicyAsync(id, policyId, cancellationToken));

    /// <summary>
    /// What a role allows (GU-96): each permission with the policies granting it, or
    /// <c>allPermissions</c> for TenantAdmin. Needs <c>auth.roles.read</c>.
    /// </summary>
    [HttpGet("{id:guid}/permissions")]
    public async Task<ActionResult<OperationResult<RolePermissionsDto>>> GetPermissions(Guid id, CancellationToken cancellationToken = default)
        => ToActionResult(await _roleService.GetPermissionsAsync(id, cancellationToken));
}
