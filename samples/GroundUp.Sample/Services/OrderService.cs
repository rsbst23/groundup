using GroundUp.Core.Results;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Repositories;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

/// <summary>
/// Order service — demonstrates the override pattern for complex entities.
/// Uses different DTOs for different operations.
/// </summary>
public class OrderService : BaseService<OrderListDto>
{
    private readonly OrderRepository _orderRepository;

    public OrderService(OrderRepository repository, IEventBus eventBus)
        : base(repository, eventBus)
    {
        _orderRepository = repository;
    }

    // GetAllAsync — inherited from BaseService, returns PaginatedData<OrderListDto>
    // DeleteAsync — inherited from BaseService, works as-is

    /// <summary>
    /// Get order detail with full customer info — different response DTO than GetAll.
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> GetDetailByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.GetDetailByIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Create order — accepts CreateOrderDto (not OrderListDto).
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> CreateOrderAsync(
        CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderRepository.CreateOrderAsync(dto, cancellationToken);

        if (result.Success)
            await PublishEventSafelyAsync(
                new EntityCreatedEvent<OrderDetailDto> { Entity = result.Data! }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Update order — accepts UpdateOrderDto (not OrderListDto).
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> UpdateOrderAsync(
        Guid id, UpdateOrderDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _orderRepository.UpdateOrderAsync(id, dto, cancellationToken);

        if (result.Success)
            await PublishEventSafelyAsync(
                new EntityUpdatedEvent<OrderDetailDto> { Entity = result.Data! }, cancellationToken);

        return result;
    }
}
