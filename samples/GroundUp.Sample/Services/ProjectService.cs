using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

public class ProjectService : BaseService
{
    private readonly IBaseRepository<ProjectDto> _repository;

    public ProjectService(
        IBaseRepository<ProjectDto> repository,
        IEventBus eventBus,
        IServiceProvider serviceProvider)
        : base(eventBus, serviceProvider)
    {
        _repository = repository;
    }

    public Task<OperationResult<PaginatedData<ProjectDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken ct = default)
        => _repository.GetAllAsync(filterParams, ct);

    public Task<OperationResult<ProjectDto>> GetByIdAsync(
        Guid id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public async Task<OperationResult<ProjectDto>> AddAsync(
        ProjectDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.AddAsync(dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityCreatedEvent<ProjectDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult<ProjectDto>> UpdateAsync(
        Guid id, ProjectDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return error;

        var result = await _repository.UpdateAsync(id, dto, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityUpdatedEvent<ProjectDto> { Entity = result.Data! }, ct);
        return result;
    }

    public async Task<OperationResult> DeleteAsync(
        Guid id, CancellationToken ct = default)
    {
        var result = await _repository.DeleteAsync(id, ct);
        if (result.Success)
            await PublishEventSafelyAsync(new EntityDeletedEvent<ProjectDto> { EntityId = id }, ct);
        return result;
    }
}
