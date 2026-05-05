using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Services;

namespace GroundUp.Tests.Unit.Api.TestHelpers;

/// <summary>
/// Concrete test service extending the non-generic BaseService.
/// Provides explicit CRUD methods that delegate to the repository,
/// mirroring the pattern used by real services (e.g., TodoItemService).
/// </summary>
public class TestBaseService : BaseService
{
    private readonly IBaseRepository<ControllerTestDto> _repository;

    public TestBaseService(
        IBaseRepository<ControllerTestDto> repository,
        IEventBus eventBus,
        IServiceProvider serviceProvider)
        : base(eventBus, serviceProvider)
    {
        _repository = repository;
    }

    public Task<OperationResult<PaginatedData<ControllerTestDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken ct = default)
        => _repository.GetAllAsync(filterParams, ct);

    public Task<OperationResult<ControllerTestDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public async Task<OperationResult<ControllerTestDto>> AddAsync(
        ControllerTestDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.AddAsync(dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityCreatedEvent<ControllerTestDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult<ControllerTestDto>> UpdateAsync(
        Guid id, ControllerTestDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.UpdateAsync(id, dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityUpdatedEvent<ControllerTestDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var result = await _repository.DeleteAsync(id, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityDeletedEvent<ControllerTestDto> { EntityId = id }, ct);
        return result;
    }
}
