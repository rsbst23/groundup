using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Sample.Dtos;

namespace GroundUp.Sample.Services;

/// <summary>
/// Service interface for order operations. Demonstrates authorization enforcement
/// via <see cref="RequiresPermissionAttribute"/> on write operations.
/// </summary>
public interface IOrderService
{
    Task<OperationResult<PaginatedData<OrderListDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken ct = default);

    Task<OperationResult<OrderDetailDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default);

    [RequiresPermission("orders.create")]
    Task<OperationResult<OrderDetailDto>> CreateAsync(
        CreateOrderDto dto, CancellationToken ct = default);

    [RequiresPermission("orders.update")]
    Task<OperationResult<OrderDetailDto>> UpdateAsync(
        Guid id, UpdateOrderDto dto, CancellationToken ct = default);

    [RequiresPermission("orders.delete")]
    Task<OperationResult> DeleteAsync(
        Guid id, CancellationToken ct = default);
}
