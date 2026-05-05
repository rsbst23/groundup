using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

public class OrdersController : BaseController
{
    private readonly OrderService _service;

    public OrdersController(OrderService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<OperationResult<PaginatedData<OrderListDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(filterParams, ct);
        if (result.Success && result.Data is not null)
            AddPaginationHeaders(result.Data);
        return ToActionResult(result);
    }

    /// <summary>
    /// Returns OrderDetailDto — the correct rich DTO for single-entity views.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> GetById(
        Guid id, CancellationToken ct = default)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Accepts CreateOrderDto — only user-provided fields, returns OrderDetailDto.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> Create(
        [FromBody] CreateOrderDto dto, CancellationToken ct = default)
    {
        var result = await _service.CreateAsync(dto, ct);
        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        return ToActionResult(result);
    }

    /// <summary>
    /// Accepts UpdateOrderDto — only changeable fields, returns OrderDetailDto.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> Update(
        Guid id, [FromBody] UpdateOrderDto dto, CancellationToken ct = default)
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
