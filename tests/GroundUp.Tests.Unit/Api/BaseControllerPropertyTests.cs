using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Tests.Unit.Api.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace GroundUp.Tests.Unit.Api;

/// <summary>
/// Property-based tests for <see cref="GroundUp.Api.Controllers.BaseController{TDto}"/>.
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
                var service = new TestBaseService(repository, eventBus);
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
                var service = new TestBaseService(repository, eventBus);
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
}
