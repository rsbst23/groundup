using FluentValidation;
using FluentValidation.Results;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Tests.Unit.Services.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services;

/// <summary>
/// Property-based tests for <see cref="GroundUp.Services.BaseService{TDto}"/>.
/// Validates that validation error mapping is lossless and order-preserving.
/// </summary>
public sealed class BaseServicePropertyTests
{
    /// <summary>
    /// Feature: phase3d-service-layer, Property 1: AddAsync validation error mapping is lossless and order-preserving.
    /// For any non-empty list of validation error messages, when a validator fails with those errors,
    /// AddAsync returns an OperationResult with Success==false, StatusCode==400,
    /// Message=="Validation failed", and Errors contains exactly the same messages in order.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AddAsync_ValidationErrorMapping_IsLosslessAndOrderPreserving(NonEmptyArray<NonNull<string>> errorMessages)
    {
        var messages = errorMessages.Get.Select(m => m.Get).ToList();

        var repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
        var eventBus = Substitute.For<IEventBus>();
        var validator = Substitute.For<IValidator<ServiceTestDto>>();

        var failures = messages.Select(msg => new ValidationFailure("Name", msg)).ToList();
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var service = new TestService(repository, eventBus, validator);
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
    /// Feature: phase3d-service-layer, Property 2: UpdateAsync validation error mapping is lossless and order-preserving.
    /// For any non-empty list of validation error messages, when a validator fails with those errors,
    /// UpdateAsync returns an OperationResult with Success==false, StatusCode==400,
    /// Message=="Validation failed", and Errors contains exactly the same messages in order.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateAsync_ValidationErrorMapping_IsLosslessAndOrderPreserving(NonEmptyArray<NonNull<string>> errorMessages)
    {
        var messages = errorMessages.Get.Select(m => m.Get).ToList();

        var repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
        var eventBus = Substitute.For<IEventBus>();
        var validator = Substitute.For<IValidator<ServiceTestDto>>();

        var failures = messages.Select(msg => new ValidationFailure("Name", msg)).ToList();
        var validationResult = new ValidationResult(failures);
        validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(validationResult));

        var service = new TestService(repository, eventBus, validator);
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "test" };

        var result = service.UpdateAsync(Guid.NewGuid(), dto).GetAwaiter().GetResult();

        return (result.Success == false
            && result.StatusCode == 400
            && result.Message == "Validation failed"
            && result.Errors != null
            && result.Errors.SequenceEqual(messages))
            .ToProperty();
    }
}
