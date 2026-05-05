using FluentValidation;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Services;

/// <summary>
/// Abstract base service providing shared infrastructure for all GroundUp
/// services: FluentValidation pipeline, event publishing, and error handling.
/// <para>
/// Derived services inject their own repository (via constructor) and define
/// methods with explicit input/output DTO types. No generic CRUD methods are
/// defined here — services own their business operations.
/// </para>
/// <para>
/// Validators are resolved from the DI container at call time, supporting
/// multiple DTO types per service (e.g., CreateOrderDto and UpdateOrderDto
/// in the same OrderService).
/// </para>
/// </summary>
public abstract class BaseService
{
    /// <summary>
    /// The event bus for publishing entity lifecycle events.
    /// </summary>
    protected IEventBus EventBus { get; }

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="BaseService"/>.
    /// </summary>
    /// <param name="eventBus">The event bus for publishing lifecycle events.</param>
    /// <param name="serviceProvider">
    /// The DI service provider for resolving FluentValidation validators at runtime.
    /// </param>
    protected BaseService(IEventBus eventBus, IServiceProvider serviceProvider)
    {
        EventBus = eventBus;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Validates a DTO using the FluentValidation validator registered in DI.
    /// Returns null if validation passes or no validator is registered for the type.
    /// Returns a BadRequest OperationResult if validation fails.
    /// </summary>
    /// <typeparam name="TDto">The DTO type to validate.</typeparam>
    /// <param name="dto">The DTO instance to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Null on success; BadRequest OperationResult on failure.</returns>
    protected async Task<OperationResult<TDto>?> ValidateAsync<TDto>(
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        var validator = _serviceProvider.GetService<IValidator<TDto>>();
        if (validator is null)
            return null;

        var validationResult = await validator.ValidateAsync(dto, cancellationToken);
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
    protected async Task PublishEventSafelyAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
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
}
