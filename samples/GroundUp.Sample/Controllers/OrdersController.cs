using GroundUp.Api.Controllers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Order controller — demonstrates the complex entity pattern.
/// <list type="bullet">
///   <item>GetAll — inherited from base, returns <see cref="OrderListDto"/> for grid display.</item>
///   <item>GetById — overridden to return <see cref="OrderDetailDto"/> with full customer info.</item>
///   <item>Create — custom endpoint accepting <see cref="CreateOrderDto"/>.</item>
///   <item>Update — custom endpoint accepting <see cref="UpdateOrderDto"/>.</item>
///   <item>Delete — inherited from base, works as-is.</item>
/// </list>
/// Base Create/Update are simply not exposed (no HTTP attribute = not routable).
/// </summary>
public class OrdersController : BaseController<OrderListDto>
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
        : base(orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Get all orders — inherited from base, returns paginated OrderListDto.
    /// </summary>
    [HttpGet]
    public override Task<ActionResult<OperationResult<PaginatedData<OrderListDto>>>> GetAll(
        [FromQuery] FilterParams filterParams, CancellationToken cancellationToken = default)
        => base.GetAll(filterParams, cancellationToken);

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

    /// <summary>
    /// Delete order — inherited from base.
    /// </summary>
    [HttpDelete("{id}")]
    public override Task<ActionResult<OperationResult>> Delete(
        Guid id, CancellationToken cancellationToken = default)
        => base.Delete(id, cancellationToken);
}
