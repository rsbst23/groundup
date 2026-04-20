using FluentValidation;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;

namespace GroundUp.Services;

/// <summary>
/// Abstract base service that wraps <see cref="IBaseRepository{TDto}"/> with
/// a Validate → Persist → Publish pipeline for mutations and pure pass-through
/// for reads. Derived services inherit CRUD orchestration, FluentValidation,
/// and entity lifecycle event publishing without boilerplate.
/// <para>
/// All public methods are virtual so derived services can override specific
/// operations to add business logic (e.g., custom authorization, cross-service
/// orchestration) while inheriting the default pipeline for others.
/// </para>
/// <para>
/// Event publishing is fire-and-forget — exceptions from <see cref="IEventBus.PublishAsync{T}"/>
/// are caught and swallowed. A failing event handler never breaks the primary CRUD flow.
/// </para>
/// </summary>
/// <typeparam name="TDto">The DTO type exposed to callers. Must be a reference type.</typeparam>
public abstract class BaseService<TDto> where TDto : class
{
    /// <summary>
    /// The repository used for data persistence operations.
    /// Exposed as protected so derived services can access it for custom queries.
    /// </summary>
    protected IBaseRepository<TDto> Repository { get; }

    /// <summary>
    /// The event bus used for publishing entity lifecycle events.
    /// Exposed as protected so derived services can publish custom events.
    /// </summary>
    protected IEventBus EventBus { get; }

    private readonly IValidator<TDto>? _validator;

    /// <summary>
    /// Initializes a new instance of <see cref="BaseService{TDto}"/>.
    /// </summary>
    /// <param name="repository">The repository for data persistence.</param>
    /// <param name="eventBus">The event bus for publishing lifecycle events.</param>
    /// <param name="validator">
    /// Optional FluentValidation validator. When null, validation is skipped
    /// for AddAsync and UpdateAsync operations.
    /// </param>
    protected BaseService(
        IBaseRepository<TDto> repository,
        IEventBus eventBus,
        IValidator<TDto>? validator = null)
    {
        Repository = repository;
        EventBus = eventBus;
        _validator = validator;
    }

    #region Read Operations (Pass-Through)

    /// <summary>
    /// Retrieves a paginated, filtered, and sorted list of DTOs.
    /// Pure pass-through — no validation, no events.
    /// </summary>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing the matching DTOs.</returns>
    public virtual Task<OperationResult<PaginatedData<TDto>>> GetAllAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
        => Repository.GetAllAsync(filterParams, cancellationToken);

    /// <summary>
    /// Retrieves a single DTO by its unique identifier.
    /// Pure pass-through — no validation, no events.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching DTO or a NotFound result.</returns>
    public virtual Task<OperationResult<TDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => Repository.GetByIdAsync(id, cancellationToken);

    #endregion

    #region Mutating Operations (Validate → Persist → Publish)

    /// <summary>
    /// Validates the DTO, persists it via the repository, and publishes an
    /// <see cref="EntityCreatedEvent{T}"/> on success. Validation is skipped
    /// if no validator was provided.
    /// </summary>
    /// <param name="dto">The DTO containing the data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created DTO with a 201 status code on success, or BadRequest on validation failure.</returns>
    public virtual async Task<OperationResult<TDto>> AddAsync(
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateAsync(dto, cancellationToken);
        if (validationError is not null)
            return validationError;

        var result = await Repository.AddAsync(dto, cancellationToken);

        if (result.Success)
            await PublishEventSafelyAsync(new EntityCreatedEvent<TDto> { Entity = result.Data! }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Validates the DTO, updates the entity via the repository, and publishes an
    /// <see cref="EntityUpdatedEvent{T}"/> on success. Validation is skipped
    /// if no validator was provided.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to update.</param>
    /// <param name="dto">The DTO containing the updated values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated DTO on success, NotFound if entity doesn't exist, or BadRequest on validation failure.</returns>
    public virtual async Task<OperationResult<TDto>> UpdateAsync(
        Guid id,
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidateAsync(dto, cancellationToken);
        if (validationError is not null)
            return validationError;

        var result = await Repository.UpdateAsync(id, dto, cancellationToken);

        if (result.Success)
            await PublishEventSafelyAsync(new EntityUpdatedEvent<TDto> { Entity = result.Data! }, cancellationToken);

        return result;
    }

    /// <summary>
    /// Deletes the entity via the repository and publishes an
    /// <see cref="EntityDeletedEvent{T}"/> on success. No validation is performed.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success or NotFound result.</returns>
    public virtual async Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Repository.DeleteAsync(id, cancellationToken);

        if (result.Success)
            await PublishEventSafelyAsync(new EntityDeletedEvent<TDto> { EntityId = id }, cancellationToken);

        return result;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Validates the DTO using the registered validator (if any).
    /// Returns null if validation passes or no validator is registered.
    /// Returns a BadRequest OperationResult if validation fails.
    /// </summary>
    private async Task<OperationResult<TDto>?> ValidateAsync(TDto dto, CancellationToken cancellationToken)
    {
        if (_validator is null)
            return null;

        var validationResult = await _validator.ValidateAsync(dto, cancellationToken);
        if (validationResult.IsValid)
            return null;

        var errors = validationResult.Errors
            .Select(e => e.ErrorMessage)
            .ToList();

        return OperationResult<TDto>.BadRequest("Validation failed", errors);
    }

    /// <summary>
    /// Publishes an event via the event bus, catching and swallowing any exceptions.
    /// Event publishing is fire-and-forget — failures never affect the operation result.
    /// </summary>
    private async Task PublishEventSafelyAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        try
        {
            await EventBus.PublishAsync(@event, cancellationToken);
        }
        catch (Exception)
        {
            // Fire-and-forget: event publishing failures do not affect the operation result
        }
    }

    #endregion
}
