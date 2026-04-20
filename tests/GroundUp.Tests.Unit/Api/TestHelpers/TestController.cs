using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Tests.Unit.Api.TestHelpers;

public class TestController : BaseController<ControllerTestDto>
{
    public TestController(BaseService<ControllerTestDto> service) : base(service) { }

    [HttpGet]
    public override Task<ActionResult<OperationResult<PaginatedData<ControllerTestDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
        => base.GetAll(filterParams, cancellationToken);

    [HttpGet("{id}")]
    public override Task<ActionResult<OperationResult<ControllerTestDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
        => base.GetById(id, cancellationToken);

    [HttpPost]
    public override Task<ActionResult<OperationResult<ControllerTestDto>>> Create(
        [FromBody] ControllerTestDto dto, CancellationToken cancellationToken = default)
        => base.Create(dto, cancellationToken);

    [HttpPut("{id}")]
    public override Task<ActionResult<OperationResult<ControllerTestDto>>> Update(
        Guid id, [FromBody] ControllerTestDto dto, CancellationToken cancellationToken = default)
        => base.Update(id, dto, cancellationToken);

    [HttpDelete("{id}")]
    public override Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
        => base.Delete(id, cancellationToken);
}
