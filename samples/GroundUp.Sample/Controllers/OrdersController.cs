using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Services;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Order controller — demonstrates the override pattern for complex entities.
/// GetAll uses base (returns OrderListDto for grid display).
/// GetById, Create, Update override with custom DTOs.
/// Delete is inherited from base.
/// </summary>
public class OrdersController : BaseController<OrderListDto>
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
        : base(orderService)
    {
        _orderService = orderService;
    }

    // GetAll — inherited from BaseController, returns PaginatedData<OrderListDto> with pagination headers
    // Delete — inherited from BaseController, works as-is

    /// <summary>
    /// Get order detail — returns OrderDetailDto with full customer info.
    /// </summary>
    [HttpGet("{id}")]
    public override async Task<ActionResult<OperationResult<OrderListDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetDetailByIdAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Hide base Create — replaced by CreateOrder below.
    /// </summary>
    [NonAction]
    public override Task<ActionResult<OperationResult<OrderListDto>>> Create(
        [FromBody] OrderListDto dto, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use CreateOrder instead");

    /// <summary>
    /// Hide base Update — replaced by UpdateOrder below.
    /// </summary>
    [NonAction]
    public override Task<ActionResult<OperationResult<OrderListDto>>> Update(
        Guid id, [FromBody] OrderListDto dto, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use UpdateOrder instead");

    /// <summary>
    /// Create order — accepts CreateOrderDto (only user-provided fields), returns OrderDetailDto.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> CreateOrder(
        [FromBody] CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.CreateOrderAsync(dto, cancellationToken);
        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update order — accepts UpdateOrderDto (only changeable fields), returns OrderDetailDto.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> UpdateOrder(
        Guid id, [FromBody] UpdateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.UpdateOrderAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }
}
