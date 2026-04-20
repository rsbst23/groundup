using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Simple CRUD controller — base classes handle everything.
/// Demonstrates the "just works" pattern for simple entities.
/// </summary>
public class CustomersController : BaseController<CustomerDto>
{
    public CustomersController(BaseService<CustomerDto> service) : base(service) { }

    [HttpGet]
    public override Task<ActionResult<OperationResult<PaginatedData<CustomerDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
        => base.GetAll(filterParams, cancellationToken);

    [HttpGet("{id}")]
    public override Task<ActionResult<OperationResult<CustomerDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => base.GetById(id, cancellationToken);

    [HttpPost]
    public override Task<ActionResult<OperationResult<CustomerDto>>> Create(
        [FromBody] CustomerDto dto, CancellationToken cancellationToken = default)
        => base.Create(dto, cancellationToken);

    [HttpPut("{id}")]
    public override Task<ActionResult<OperationResult<CustomerDto>>> Update(
        Guid id, [FromBody] CustomerDto dto, CancellationToken cancellationToken = default)
        => base.Update(id, dto, cancellationToken);

    [HttpDelete("{id}")]
    public override Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
        => base.Delete(id, cancellationToken);
}
