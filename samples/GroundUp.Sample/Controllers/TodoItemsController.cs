using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Simple CRUD controller — demonstrates the "just works" pattern.
/// One-line overrides add HTTP attributes to expose base class methods.
/// </summary>
public class TodoItemsController : BaseController<TodoItemDto>
{
    public TodoItemsController(BaseService<TodoItemDto> service) : base(service) { }

    [HttpGet]
    public override Task<ActionResult<OperationResult<PaginatedData<TodoItemDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
        => base.GetAll(filterParams, cancellationToken);

    [HttpGet("{id}")]
    public override Task<ActionResult<OperationResult<TodoItemDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => base.GetById(id, cancellationToken);

    [HttpPost]
    public override Task<ActionResult<OperationResult<TodoItemDto>>> Create(
        [FromBody] TodoItemDto dto, CancellationToken cancellationToken = default)
        => base.Create(dto, cancellationToken);

    [HttpPut("{id}")]
    public override Task<ActionResult<OperationResult<TodoItemDto>>> Update(
        Guid id, [FromBody] TodoItemDto dto, CancellationToken cancellationToken = default)
        => base.Update(id, dto, cancellationToken);

    [HttpDelete("{id}")]
    public override Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
        => base.Delete(id, cancellationToken);
}
