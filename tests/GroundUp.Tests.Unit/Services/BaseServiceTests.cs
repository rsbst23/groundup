using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Tests.Unit.Services.TestHelpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GroundUp.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="GroundUp.Services.BaseService"/>
/// covering AddAsync, UpdateAsync, DeleteAsync, read operations,
/// ValidateAsync resolution from IServiceProvider, and PublishEventSafelyAsync.
/// </summary>
public sealed class BaseServiceTests
{
    private readonly IBaseRepository<ServiceTestDto> _repository = Substitute.For<IBaseRepository<ServiceTestDto>>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly IValidator<ServiceTestDto> _validator = Substitute.For<IValidator<ServiceTestDto>>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IServiceProvider _serviceProviderNoValidator = Substitute.For<IServiceProvider>();
    private readonly TestService _service;
    private readonly TestService _serviceNoValidator;

    public BaseServiceTests()
    {
        // Service provider that returns a validator
        _serviceProvider.GetService(typeof(IValidator<ServiceTestDto>))
            .Returns(_validator);

        // Service provider that returns no validator
        _serviceProviderNoValidator.GetService(typeof(IValidator<ServiceTestDto>))
            .Returns(null);

        _service = new TestService(_repository, _eventBus, _serviceProvider);
        _serviceNoValidator = new TestService(_repository, _eventBus, _serviceProviderNoValidator);
    }

    #region Structure Tests

    [Fact]
    public void BaseService_IsNotGeneric()
    {
        var type = typeof(GroundUp.Services.BaseService);
        Assert.False(type.IsGenericType);
        Assert.False(type.IsGenericTypeDefinition);
    }

    [Fact]
    public void BaseService_HasNoCrudMethods()
    {
        var type = typeof(GroundUp.Services.BaseService);
        var publicMethods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        var crudMethodNames = new[] { "GetAllAsync", "GetByIdAsync", "AddAsync", "UpdateAsync", "DeleteAsync" };
        var foundCrudMethods = publicMethods.Where(m => crudMethodNames.Contains(m.Name)).ToList();
        Assert.Empty(foundCrudMethods);
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_NoValidatorRegistered_SkipsValidationAndCallsRepo()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        var result = await _serviceNoValidator.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
        await _repository.Received(1).AddAsync(dto, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateAsync_ValidatorRegistered_ValidationPasses_CallsRepo()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        var result = await _service.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
    }

    [Fact]
    public async Task ValidateAsync_ValidatorRegistered_ValidationFails_ReturnsBadRequest()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "" };
        var failures = new List<ValidationFailure>
        {
            new("Name", "Name is required"),
            new("Name", "Name must be at least 2 characters")
        };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        var result = await _service.AddAsync(dto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("Validation failed");
        result.Errors.Should().ContainInOrder("Name is required", "Name must be at least 2 characters");
    }

    [Fact]
    public async Task ValidateAsync_ValidationFails_DoesNotCallRepository()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "" };
        var failures = new List<ValidationFailure> { new("Name", "Name is required") };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        await _service.AddAsync(dto);

        // Assert
        await _repository.DidNotReceive().AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ValidationPassesAndRepoSucceeds_ReturnsOkWithDto()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        var result = await _service.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
    }

    [Fact]
    public async Task AddAsync_RepoSucceeds_PublishesEntityCreatedEvent()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        await _service.AddAsync(dto);

        // Assert
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityCreatedEvent<ServiceTestDto>>(e => e.Entity == dto),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ValidationFails_DoesNotPublishEvent()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "" };
        var failures = new List<ValidationFailure> { new("Name", "Name is required") };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        await _service.AddAsync(dto);

        // Assert
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<EntityCreatedEvent<ServiceTestDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_RepoFails_DoesNotPublishEvent()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.NotFound());

        // Act
        await _service.AddAsync(dto);

        // Assert
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<EntityCreatedEvent<ServiceTestDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_EventBusThrows_ReturnsSuccessfulResult()
    {
        // Arrange
        var dto = new ServiceTestDto { Id = Guid.NewGuid(), Name = "Test" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.AddAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));
        _eventBus.PublishAsync(Arg.Any<EntityCreatedEvent<ServiceTestDto>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("event bus failed"));

        // Act
        var result = await _service.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_ValidationPassesAndRepoSucceeds_ReturnsOkWithDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Updated" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        var result = await _service.UpdateAsync(id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
    }

    [Fact]
    public async Task UpdateAsync_RepoSucceeds_PublishesEntityUpdatedEvent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Updated" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        await _service.UpdateAsync(id, dto);

        // Assert
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityUpdatedEvent<ServiceTestDto>>(e => e.Entity == dto),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ValidationFails_ReturnsBadRequestWithErrors()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "" };
        var failures = new List<ValidationFailure>
        {
            new("Name", "Name is required"),
            new("Name", "Name must be at least 2 characters")
        };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        var result = await _service.UpdateAsync(id, dto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("Validation failed");
        result.Errors.Should().ContainInOrder("Name is required", "Name must be at least 2 characters");
    }

    [Fact]
    public async Task UpdateAsync_ValidationFails_DoesNotCallRepository()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "" };
        var failures = new List<ValidationFailure> { new("Name", "Name is required") };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        await _service.UpdateAsync(id, dto);

        // Assert
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ValidationFails_DoesNotPublishEvent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "" };
        var failures = new List<ValidationFailure> { new("Name", "Name is required") };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(failures));

        // Act
        await _service.UpdateAsync(id, dto);

        // Assert
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<EntityUpdatedEvent<ServiceTestDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NoValidator_SkipsValidationAndCallsRepo()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Updated" };
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));

        // Act
        var result = await _serviceNoValidator.UpdateAsync(id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
        await _repository.Received(1).UpdateAsync(id, dto, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RepoFails_DoesNotPublishEvent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Updated" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.NotFound());

        // Act
        await _service.UpdateAsync(id, dto);

        // Assert
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<EntityUpdatedEvent<ServiceTestDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_EventBusThrows_ReturnsSuccessfulResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Updated" };
        _validator.ValidateAsync(Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ServiceTestDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(dto));
        _eventBus.PublishAsync(Arg.Any<EntityUpdatedEvent<ServiceTestDto>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("event bus failed"));

        // Act
        var result = await _service.UpdateAsync(id, dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(dto);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_RepoSucceeds_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        // Act
        var result = await _service.DeleteAsync(id);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RepoSucceeds_PublishesEntityDeletedEventWithCorrectId()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        // Act
        await _service.DeleteAsync(id);

        // Assert
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityDeletedEvent<ServiceTestDto>>(e => e.EntityId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RepoFails_DoesNotPublishEvent()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Error", 500));

        // Act
        await _service.DeleteAsync(id);

        // Assert
        await _eventBus.DidNotReceive().PublishAsync(
            Arg.Any<EntityDeletedEvent<ServiceTestDto>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_RepoReturnsNotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.NotFound());

        // Act
        var result = await _service.DeleteAsync(id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_EventBusThrows_ReturnsSuccessfulResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());
        _eventBus.PublishAsync(Arg.Any<EntityDeletedEvent<ServiceTestDto>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("event bus failed"));

        // Act
        var result = await _service.DeleteAsync(id);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Read Operation Tests

    [Fact]
    public async Task GetAllAsync_ReturnsRepositoryResultUnchanged()
    {
        // Arrange
        var filterParams = new FilterParams();
        var paginatedData = new PaginatedData<ServiceTestDto>
        {
            Items = new List<ServiceTestDto> { new() { Id = Guid.NewGuid(), Name = "Test" } },
            PageNumber = 1,
            PageSize = 10,
            TotalRecords = 1
        };
        var expected = OperationResult<PaginatedData<ServiceTestDto>>.Ok(paginatedData);
        _repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.GetAllAsync(filterParams);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetAllAsync_DoesNotInvokeValidator()
    {
        // Arrange
        var filterParams = new FilterParams();
        _repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PaginatedData<ServiceTestDto>>.Ok(
                new PaginatedData<ServiceTestDto>
                {
                    Items = new List<ServiceTestDto>(),
                    PageNumber = 1,
                    PageSize = 10,
                    TotalRecords = 0
                }));

        // Act
        await _service.GetAllAsync(filterParams);

        // Assert
        await _validator.DidNotReceive().ValidateAsync(
            Arg.Any<ServiceTestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_DoesNotInvokeEventBus()
    {
        // Arrange
        var filterParams = new FilterParams();
        _repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PaginatedData<ServiceTestDto>>.Ok(
                new PaginatedData<ServiceTestDto>
                {
                    Items = new List<ServiceTestDto>(),
                    PageNumber = 1,
                    PageSize = 10,
                    TotalRecords = 0
                }));

        // Act
        await _service.GetAllAsync(filterParams);

        // Assert
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<IEvent>(default!, default);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsRepositoryResultUnchanged()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ServiceTestDto { Id = id, Name = "Test" };
        var expected = OperationResult<ServiceTestDto>.Ok(dto);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.GetByIdAsync(id);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotInvokeValidator()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(new ServiceTestDto { Id = id, Name = "Test" }));

        // Act
        await _service.GetByIdAsync(id);

        // Assert
        await _validator.DidNotReceive().ValidateAsync(
            Arg.Any<ServiceTestDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotInvokeEventBus()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ServiceTestDto>.Ok(new ServiceTestDto { Id = id, Name = "Test" }));

        // Act
        await _service.GetByIdAsync(id);

        // Assert
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<IEvent>(default!, default);
    }

    #endregion
}
