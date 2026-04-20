using GroundUp.Api.Controllers;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Services;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Order controller — demonstrates the override pattern for complex entities.
/// GetAll uses base (returns OrderListDto), but GetById/Create/Update use custom methods
/// with different DTOs.
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
    /// Uses 'new' to hide the base GetById since the return type differs.
    /// </summary>
    [HttpGet("{id}")]
    public new async Task<ActionResult<OperationResult<OrderDetailDto>>> GetById(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetDetailByIdAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Create order — accepts CreateOrderDto, returns OrderDetailDto.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> Create(
        [FromBody] CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.CreateOrderAsync(dto, cancellationToken);
        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update order — accepts UpdateOrderDto, returns OrderDetailDto.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<OperationResult<OrderDetailDto>>> Update(
        Guid id, [FromBody] UpdateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderService.UpdateOrderAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }
}
