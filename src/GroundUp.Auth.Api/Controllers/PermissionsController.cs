using GroundUp.Api.Controllers;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Policies;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// The permission catalogue (GU-96): every permission a tenant policy can grant, grouped by module.
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
    /// Lists the catalogue grouped by module, without system-only keys. Needs <c>auth.permissions.read</c>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OperationResult<List<PermissionModuleDto>>>> List(CancellationToken cancellationToken = default)
        => ToActionResult(await _policyService.GetCatalogAsync(cancellationToken));
}
