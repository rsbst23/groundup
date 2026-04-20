using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Tests.Unit.Api.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api;

/// <summary>
/// Unit tests for <see cref="GroundUp.Api.Controllers.BaseController{TDto}"/>.
/// Validates ToActionResult mapping and CRUD endpoint behavior using
/// a concrete TestController backed by a mocked service.
/// </summary>
public sealed class BaseControllerTests
{
    private readonly IBaseRepository<ControllerTestDto> _repository;
    private readonly IEventBus _eventBus;
    private readonly TestBaseService _service;
    private readonly TestController _controller;

    public BaseControllerTests()
    {
        _repository = Substitute.For<IBaseRepository<ControllerTestDto>>();
        _eventBus = Substitute.For<IEventBus>();
        _service = new TestBaseService(_repository, _eventBus);
        _controller = new TestController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    #region ToActionResult Mapping Tests

    [Fact]
    public async Task ToActionResult_StatusCode200_ReturnsOkObjectResult()
    {
        // Arrange
        var dto = new ControllerTestDto { Id = Guid.NewGuid(), Name = "Test" };
        var operationResult = OperationResult<ControllerTestDto>.Ok(dto);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(dto.Id);

        // Assert
        var actionResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, actionResult.StatusCode);
    }

    [Fact]
    public async Task ToActionResult_StatusCode201_Returns201StatusCode()
    {
        // Arrange
        var dto = new ControllerTestDto { Id = Guid.NewGuid(), Name = "Test" };
        var operationResult = OperationResult<ControllerTestDto>.Ok(dto, statusCode: 201);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(dto.Id);

        // Assert
        var actionResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, actionResult.StatusCode);
    }

    [Fact]
    public async Task ToActionResult_StatusCode400_ReturnsBadRequestObjectResult()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.BadRequest("Validation failed");
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToActionResult_StatusCode401_ReturnsUnauthorizedResult()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.Unauthorized();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task ToActionResult_StatusCode403_Returns403StatusCode()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.Forbidden();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        var actionResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, actionResult.StatusCode);
    }

    [Fact]
    public async Task ToActionResult_StatusCode404_ReturnsNotFoundObjectResult()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.NotFound();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToActionResult_UnmappedStatusCode409_ReturnsObjectResultWithCorrectStatusCode()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.Fail("Conflict", 409, "CONFLICT");
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        var actionResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, actionResult.StatusCode);
    }

    #endregion

    #region CRUD Endpoint Tests

    [Fact]
    public async Task GetAll_ServiceReturnsSuccess_ReturnsOkWithPaginationHeaders()
    {
        // Arrange
        var paginatedData = new PaginatedData<ControllerTestDto>
        {
            Items = new List<ControllerTestDto>
            {
                new() { Id = Guid.NewGuid(), Name = "Item1" }
            },
            PageNumber = 2,
            PageSize = 10,
            TotalRecords = 25
        };
        var operationResult = OperationResult<PaginatedData<ControllerTestDto>>.Ok(paginatedData);
        _repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        var filterParams = new FilterParams();

        // Act
        var result = await _controller.GetAll(filterParams);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);

        var headers = _controller.Response.Headers;
        Assert.Equal("25", headers["X-Total-Count"].ToString());
        Assert.Equal("2", headers["X-Page-Number"].ToString());
        Assert.Equal("10", headers["X-Page-Size"].ToString());
        Assert.Equal("3", headers["X-Total-Pages"].ToString());
    }

    [Fact]
    public async Task GetAll_ServiceReturnsFailed_DoesNotAddPaginationHeaders()
    {
        // Arrange
        var operationResult = OperationResult<PaginatedData<ControllerTestDto>>.Fail("Error", 500);
        _repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        var filterParams = new FilterParams();

        // Act
        await _controller.GetAll(filterParams);

        // Assert
        var headers = _controller.Response.Headers;
        Assert.False(headers.ContainsKey("X-Total-Count"));
        Assert.False(headers.ContainsKey("X-Page-Number"));
        Assert.False(headers.ContainsKey("X-Page-Size"));
        Assert.False(headers.ContainsKey("X-Total-Pages"));
    }

    [Fact]
    public async Task GetById_ServiceReturnsSuccess_ReturnsOk()
    {
        // Arrange
        var dto = new ControllerTestDto { Id = Guid.NewGuid(), Name = "Test" };
        var operationResult = OperationResult<ControllerTestDto>.Ok(dto);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(dto.Id);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ServiceReturns404_ReturnsNotFound()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.NotFound();
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.GetById(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ServiceReturns201_ReturnsCreatedAtAction()
    {
        // Arrange
        var dto = new ControllerTestDto { Id = Guid.NewGuid(), Name = "New" };
        var operationResult = OperationResult<ControllerTestDto>.Ok(dto, statusCode: 201);
        _repository.AddAsync(Arg.Any<ControllerTestDto>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Create(dto);

        // Assert
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Create_ServiceReturns400_ReturnsBadRequest()
    {
        // Arrange
        var dto = new ControllerTestDto { Id = Guid.NewGuid(), Name = "" };
        var operationResult = OperationResult<ControllerTestDto>.BadRequest("Validation failed");
        _repository.AddAsync(Arg.Any<ControllerTestDto>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Create(dto);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_ServiceReturnsSuccess_ReturnsOk()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new ControllerTestDto { Id = id, Name = "Updated" };
        var operationResult = OperationResult<ControllerTestDto>.Ok(dto);
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ControllerTestDto>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Update(id, dto);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_ServiceReturns404_ReturnsNotFound()
    {
        // Arrange
        var operationResult = OperationResult<ControllerTestDto>.NotFound();
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<ControllerTestDto>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Update(Guid.NewGuid(), new ControllerTestDto());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ServiceReturnsSuccess_ReturnsOk()
    {
        // Arrange
        var operationResult = OperationResult.Ok();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Delete(Guid.NewGuid());

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_ServiceReturns404_ReturnsNotFound()
    {
        // Arrange
        var operationResult = OperationResult.NotFound();
        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(operationResult);

        // Act
        var result = await _controller.Delete(Guid.NewGuid());

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    #endregion
}
