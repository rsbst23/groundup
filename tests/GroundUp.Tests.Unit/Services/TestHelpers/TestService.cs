using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Services;

namespace GroundUp.Tests.Unit.Services.TestHelpers;

/// <summary>
/// Concrete test service extending the non-generic BaseService.
/// Provides explicit CRUD methods that delegate to the repository,
/// mirroring the pattern used by real services (e.g., TodoItemService).
/// </summary>
public class TestService : BaseService
{
    private readonly IBaseRepository<ServiceTestDto> _repository;

    public TestService(
        IBaseRepository<ServiceTestDto> repository,
        IEventBus eventBus,
        IServiceProvider serviceProvider)
        : base(eventBus, serviceProvider)
    {
        _repository = repository;
    }

    public Task<OperationResult<PaginatedData<ServiceTestDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken ct = default)
        => _repository.GetAllAsync(filterParams, ct);

    public Task<OperationResult<ServiceTestDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public async Task<OperationResult<ServiceTestDto>> AddAsync(
        ServiceTestDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.AddAsync(dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityCreatedEvent<ServiceTestDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult<ServiceTestDto>> UpdateAsync(
        Guid id, ServiceTestDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.UpdateAsync(id, dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityUpdatedEvent<ServiceTestDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var result = await _repository.DeleteAsync(id, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityDeletedEvent<ServiceTestDto> { EntityId = id }, ct);
        return result;
    }
}
