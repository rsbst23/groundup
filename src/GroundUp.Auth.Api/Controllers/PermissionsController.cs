using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Policies;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// The permission catalogue (GU-96, paged in GU-97d): every permission a tenant policy can grant.
/// Read-only; permissions are defined by the modules that check them. A thin HTTP adapter over
/// <see cref="IPolicyService"/>. Under <c>/api</c>, so the host/token tenant check applies.
/// </summary>
[Route("api/permissions")]
public sealed class PermissionsController : BaseController
{
    private readonly IPolicyService _policyService;

    /// <summary>
    /// Initializes a new instance of <see cref="PermissionsController"/>.
    /// </summary>
    public PermissionsController(IPolicyService policyService)
    {
        _policyService = policyService;
    }

    /// <summary>
    /// A page of the catalogue, without System-only keys. Needs <c>auth.permissions.read</c>. Accepts
    /// <c>FilterParams</c>: <c>sortBy</c> (name, key, module), <c>filters[module]</c>, <c>searchTerm</c> (name,
    /// key, description); other fields are ignored. By module then key unless sorted. Pagination metadata
    /// is also returned in the headers.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<PermissionDto>>>> List(
        [FromQuery] FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var result = await _policyService.ListCatalogAsync(filterParams, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            AddPaginationHeaders(result.Data);
        }

        return ToActionResult(result);
    }

    /// <summary>The catalogue's modules, sorted (for a filter). Needs <c>auth.permissions.read</c>.</summary>
    [HttpGet("modules")]
    public async Task<ActionResult<OperationResult<List<string>>>> Modules(CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.GetModulesAsync(cancellationToken));
}
