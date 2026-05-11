using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Repositories;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

public class OrderService : BaseService, IOrderService
{
    private readonly OrderRepository _repository;

    public OrderService(
        OrderRepository repository,
        IEventBus eventBus,
        IServiceProvider serviceProvider)
        : base(eventBus, serviceProvider)
    {
        _repository = repository;
    }

    public async Task<OperationResult<PaginatedData<OrderListDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken ct = default)
        => await _repository.GetAllAsync(filterParams, ct);

    public async Task<OperationResult<OrderDetailDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
        => await _repository.GetDetailByIdAsync(id, ct);

    public async Task<OperationResult<OrderDetailDto>> CreateAsync(
        CreateOrderDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null)
            return OperationResult<OrderDetailDto>.BadRequest(error.Message, error.Errors);

        var result = await _repository.CreateOrderAsync(dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(
                new EntityCreatedEvent<OrderDetailDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult<OrderDetailDto>> UpdateAsync(
        Guid id, UpdateOrderDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null)
            return OperationResult<OrderDetailDto>.BadRequest(error.Message, error.Errors);

        var result = await _repository.UpdateOrderAsync(id, dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(
                new EntityUpdatedEvent<OrderDetailDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var result = await _repository.DeleteAsync(id, ct);
        if (result.Success)
            await PublishEventSafelyAsync(
                new EntityDeletedEvent<OrderDetailDto> { EntityId = id }, ct);
        return result;
    }
}
