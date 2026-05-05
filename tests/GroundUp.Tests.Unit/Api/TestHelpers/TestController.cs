using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Tests.Unit.Api.TestHelpers;

/// <summary>
/// Concrete test controller extending the non-generic BaseController.
/// Defines explicit CRUD endpoints that delegate to TestBaseService,
/// mirroring the pattern used by real controllers (e.g., TodoItemsController).
/// </summary>
public class TestController : BaseController
{
    private readonly TestBaseService _service;

    public TestController(TestBaseService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<ControllerTestDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetAllAsync(filterParams, cancellationToken);
        if (result.Success && result.Data is not null)
            AddPaginationHeaders(result.Data);
        return ToActionResult(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<ControllerTestDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<ControllerTestDto>>> Create(
        [FromBody] ControllerTestDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _service.AddAsync(dto, cancellationToken);
        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        return ToActionResult(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<ControllerTestDto>>> Update(
        Guid id, [FromBody] ControllerTestDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _service.UpdateAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }
}
