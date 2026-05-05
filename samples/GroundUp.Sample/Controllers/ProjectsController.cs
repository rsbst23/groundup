using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

public class ProjectsController : BaseController
{
    private readonly ProjectService _service;

    public ProjectsController(ProjectService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<ProjectDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(filterParams, ct);
        if (result.Success && result.Data is not null)
            AddPaginationHeaders(result.Data);
        return ToActionResult(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<ProjectDto>>> GetById(
        Guid id, CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return ToActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<OperationResult<ProjectDto>>> Create(
        [FromBody] ProjectDto dto, CancellationToken ct = default)
    {
        var result = await _service.AddAsync(dto, ct);
        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        return ToActionResult(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<ProjectDto>>> Update(
        Guid id, [FromBody] ProjectDto dto, CancellationToken ct = default)
    {
        var result = await _service.UpdateAsync(id, dto, ct);
        return ToActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken ct = default)
    {
        var result = await _service.DeleteAsync(id, ct);
        return ToActionResult(result);
    }
}
