using FluentValidation;
using FluentValidation.Results;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Tests.Unit.Services.TestHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Services;

/// <summary>
/// Property-based tests for <see cref="GroundUp.Services.BaseService"/>.
/// Validates that validation error mapping is lossless and order-preserving,
/// that ValidateAsync resolves the correct validator per type, and that
/// PublishEventSafelyAsync swallows exceptions.
/// </summary>
public sealed class BaseServicePropertyTests
{
    /// <summary>
    /// Property 3: ValidateAsync correctness — AddAsync validation error mapping is lossless and order-preserving.
    /// For any non-empty list of validation error messages, when a validator fails with those errors,
    /// AddAsync returns an OperationResult with Success==false, StatusCode==400,
    /// Message=="Validation failed", and Errors contains exactly the same messages in order.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AddAsync_ValidationErrorMapping_IsLosslessAndOrderPreserving(NonEmptyArray<NonNull<string>> errorMessages)
    {
        var messages = errorMessages.Get.Select(m => m.Get).ToList();

        var repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
        var eventBus = Substitute.For<IEventBus>();
        var validator = Substitute.For<IValidator<ServiceTestDto>>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IValidator<ServiceTestDto>)).Returns(validator);

        var failures = messages.Select(msg => new ValidationFailure("Name", msg)).ToList();
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var service = new TestService(repository, eventBus, serviceProvider);
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "test" };

        var result = service.AddAsync(dto).GetAwaiter().GetResult();

        return (result.Success == false
            && result.StatusCode == 400
            && result.Message == "Validation failed"
            && result.Errors != null
            && result.Errors.SequenceEqual(messages))
            .ToProperty();
    }

    /// <summary>
    /// Property 4: ValidateAsync resolves correct validator per type — UpdateAsync validation error mapping.
    /// For any non-empty list of validation error messages, when a validator fails with those errors,
    /// UpdateAsync returns an OperationResult with Success==false, StatusCode==400,
    /// Message=="Validation failed", and Errors contains exactly the same messages in order.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateAsync_ValidationErrorMapping_IsLosslessAndOrderPreserving(NonEmptyArray<NonNull<string>> errorMessages)
    {
        var messages = errorMessages.Get.Select(m => m.Get).ToList();

        var repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
        var eventBus = Substitute.For<IEventBus>();
        var validator = Substitute.For<IValidator<ServiceTestDto>>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IValidator<ServiceTestDto>)).Returns(validator);

        var failures = messages.Select(msg => new ValidationFailure("Name", msg)).ToList();
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var service = new TestService(repository, eventBus, serviceProvider);
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "test" };

        var result = service.UpdateAsync(Guid.NewGuid(), dto).GetAwaiter().GetResult();

        return (result.Success == false
            && result.StatusCode == 400
            && result.Message == "Validation failed"
            && result.Errors != null
            && result.Errors.SequenceEqual(messages))
            .ToProperty();
    }

    /// <summary>
    /// Property 5: PublishEventSafelyAsync swallows exceptions.
    /// For any successful Add operation, even when the event bus throws,
    /// the result is still successful.
    ///
    /// **Validates: Requirements 2.7, 11.1, 11.4, 11.5**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property AddAsync_EventBusThrows_StillReturnsSuccess(NonNull<string> exceptionMessage)
    {
        var repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
        var eventBus = Substitute.For<IEventBus>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        // No validator registered — skip validation
        serviceProvider.GetService(typeof(IValidator<ServiceTestDto>)).Returns(null);

        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "test" };
        repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(GroundUp.Core.Results.OperationResult<ServiceTestDto>.Ok(dto));
        eventBus.PublishAsync(Arg.Any<EntityCreatedEvent<ServiceTestDto>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(exceptionMessage.Get));

        var service = new TestService(repository, eventBus, serviceProvider);

        var result = service.AddAsync(dto).GetAwaiter().GetResult();

        return (result.Success == true && result.Data == dto).ToProperty();
    }
}
