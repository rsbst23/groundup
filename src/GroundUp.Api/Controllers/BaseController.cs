using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Api.Controllers;

/// <summary>
/// Abstract base controller providing shared HTTP infrastructure for all
/// GroundUp API controllers. Provides ToActionResult helpers for mapping
/// OperationResult to ActionResult, and pagination header helpers.
/// <para>
/// Derived controllers inject their own service and define endpoint methods
/// with explicit HTTP attributes and operation-specific DTO types.
/// No generic CRUD methods are defined here — controllers own their endpoints.
/// </para>
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
    #region Protected Helpers

    /// <summary>
    /// Maps a generic OperationResult to the appropriate ActionResult based on StatusCode.
    /// </summary>
    protected ActionResult ToActionResult<T>(OperationResult<T> result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result),
            201 => StatusCode(201, result),
            400 => BadRequest(result),
            401 => Unauthorized(),
            403 => StatusCode(403, result),
            404 => NotFound(result),
            _ => new ObjectResult(result) { StatusCode = result.StatusCode }
        };
    }

    /// <summary>
    /// Maps a non-generic OperationResult to the appropriate ActionResult based on StatusCode.
    /// </summary>
    protected ActionResult ToActionResult(OperationResult result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result),
            201 => StatusCode(201, result),
            400 => BadRequest(result),
            401 => Unauthorized(),
            403 => StatusCode(403, result),
            404 => NotFound(result),
            _ => new ObjectResult(result) { StatusCode = result.StatusCode }
        };
    }

    /// <summary>
    /// Adds standard pagination headers to the HTTP response.
    /// Call this from GetAll-style endpoints after a successful paginated query.
    /// </summary>
    /// <typeparam name="T">The DTO type in the paginated result.</typeparam>
    /// <param name="paginatedData">The paginated data containing page metadata.</param>
    protected void AddPaginationHeaders<T>(PaginatedData<T> paginatedData)
    {
        Response.Headers["X-Total-Count"] = paginatedData.TotalRecords.ToString();
        Response.Headers["X-Page-Number"] = paginatedData.PageNumber.ToString();
        Response.Headers["X-Page-Size"] = paginatedData.PageSize.ToString();
        Response.Headers["X-Total-Pages"] = paginatedData.TotalPages.ToString();
    }

    #endregion
}
