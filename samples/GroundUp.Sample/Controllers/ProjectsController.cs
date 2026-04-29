using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Tenant-scoped CRUD controller — demonstrates the BaseTenantRepository pattern.
/// One-line overrides add HTTP attributes to expose base class methods.
/// </summary>
public class ProjectsController : BaseController<ProjectDto>
{
    public ProjectsController(BaseService<ProjectDto> service) : base(service) { }

    [HttpGet]
    public override Task<ActionResult<OperationResult<PaginatedData<ProjectDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
        => base.GetAll(filterParams, cancellationToken);

    [HttpGet("{id}")]
    public override Task<ActionResult<OperationResult<ProjectDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => base.GetById(id, cancellationToken);

    [HttpPost]
    public override Task<ActionResult<OperationResult<ProjectDto>>> Create(
        [FromBody] ProjectDto dto, CancellationToken cancellationToken = default)
        => base.Create(dto, cancellationToken);

    [HttpPut("{id}")]
    public override Task<ActionResult<OperationResult<ProjectDto>>> Update(
        Guid id, [FromBody] ProjectDto dto, CancellationToken cancellationToken = default)
        => base.Update(id, dto, cancellationToken);

    [HttpDelete("{id}")]
    public override Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
        => base.Delete(id, cancellationToken);
}
