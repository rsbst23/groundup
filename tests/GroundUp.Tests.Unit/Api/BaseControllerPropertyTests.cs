using FsCheck;
using FsCheck.Xunit;
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
/// Property-based tests for <see cref="GroundUp.Api.Controllers.BaseController"/>.
/// Validates that ToActionResult preserves status codes across the full HTTP range.
/// </summary>
public sealed class BaseControllerPropertyTests
{
    private static int ExtractStatusCode(ActionResult? actionResult)
    {
        return actionResult switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? 200,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => throw new InvalidOperationException($"Unexpected ActionResult type: {actionResult?.GetType().Name}")
        };
    }

    /// <summary>
    /// Property 1: Generic OperationResult-to-ActionResult status code preservation.
    /// For any status code in range 200–599, the ActionResult produced by GetById
    /// carries the same HTTP status code as the OperationResult.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetById_AnyStatusCode_ActionResultPreservesStatusCode()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(200, 599)),
            statusCode =>
            {
                var repository = Substitute.For<IBaseRepository<ControllerTestDto>>();
                var eventBus = Substitute.For<IEventBus>();
                var serviceProvider = Substitute.For<IServiceProvider>();
                var service = new TestBaseService(repository, eventBus, serviceProvider);
                var controller = new TestController(service)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext()
                    }
                };

                var operationResult = new OperationResult<ControllerTestDto>
                {
                    StatusCode = statusCode,
                    Success = statusCode >= 200 && statusCode < 300
                };
                repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(operationResult);

                var result = controller.GetById(Guid.NewGuid()).GetAwaiter().GetResult();
                var actualStatusCode = ExtractStatusCode(result.Result);

                return (actualStatusCode == statusCode).ToProperty();
            });
    }

    /// <summary>
    /// Property 2: Non-generic OperationResult-to-ActionResult status code preservation.
    /// For any status code in range 200–599, the ActionResult produced by Delete
    /// carries the same HTTP status code as the OperationResult.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Delete_AnyStatusCode_ActionResultPreservesStatusCode()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(200, 599)),
            statusCode =>
            {
                var repository = Substitute.For<IBaseRepository<ControllerTestDto>>();
                var eventBus = Substitute.For<IEventBus>();
                var serviceProvider = Substitute.For<IServiceProvider>();
                var service = new TestBaseService(repository, eventBus, serviceProvider);
                var controller = new TestController(service)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext()
                    }
                };

                var operationResult = new OperationResult
                {
                    StatusCode = statusCode,
                    Success = statusCode >= 200 && statusCode < 300
                };
                repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(operationResult);

                var result = controller.Delete(Guid.NewGuid()).GetAwaiter().GetResult();
                var actualStatusCode = ExtractStatusCode(result.Result);

                return (actualStatusCode == statusCode).ToProperty();
            });
    }

    /// <summary>
    /// Property 3: AddPaginationHeaders round-trip.
    /// For any valid pagination values, the headers set by AddPaginationHeaders
    /// contain the exact values from the PaginatedData.
    ///
    /// **Validates: Requirements 1.5, 1.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAll_PaginationHeaders_RoundTrip()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 1000)),
            Arb.From(Gen.Choose(1, 100)),
            Arb.From(Gen.Choose(0, 10000)),
            (pageNumber, pageSize, totalRecords) =>
            {
                var repository = Substitute.For<IBaseRepository<ControllerTestDto>>();
                var eventBus = Substitute.For<IEventBus>();
                var serviceProvider = Substitute.For<IServiceProvider>();
                var service = new TestBaseService(repository, eventBus, serviceProvider);
                var controller = new TestController(service)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext()
                    }
                };

                var paginatedData = new PaginatedData<ControllerTestDto>
                {
                    Items = new List<ControllerTestDto>(),
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalRecords
                };
                var operationResult = OperationResult<PaginatedData<ControllerTestDto>>.Ok(paginatedData);
                repository.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>())
                    .Returns(operationResult);

                controller.GetAll(new FilterParams()).GetAwaiter().GetResult();

                var headers = controller.Response.Headers;
                var expectedTotalPages = (int)Math.Ceiling((double)totalRecords / pageSize);

                return (headers["X-Total-Count"].ToString() == totalRecords.ToString()
                    && headers["X-Page-Number"].ToString() == pageNumber.ToString()
                    && headers["X-Page-Size"].ToString() == pageSize.ToString()
                    && headers["X-Total-Pages"].ToString() == expectedTotalPages.ToString())
                    .ToProperty();
            });
    }
}
