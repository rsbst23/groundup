using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Services;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Api.Controllers;

/// <summary>
/// Abstract base controller wrapping <see cref="BaseService{TDto}"/> with standard
/// CRUD methods. Controllers are thin HTTP adapters — zero business logic,
/// zero security checks. All operations delegate to the service layer.
/// <para>
/// HTTP method attributes are intentionally omitted from the base class.
/// Derived controllers add <c>[HttpGet]</c>, <c>[HttpPost]</c>, etc. on the
/// methods they want to expose. For simple entities, one-line overrides are
/// sufficient. For complex entities, controllers can skip base methods entirely
/// and define custom endpoints with different DTO types.
/// </para>
/// </summary>
/// <typeparam name="TDto">The DTO type exposed to API consumers. Must be a reference type.</typeparam>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseController<TDto> : ControllerBase where TDto : class
{
    /// <summary>
    /// The service used for all CRUD operations.
    /// Exposed as protected so derived controllers can access it for custom endpoints.
    /// </summary>
    protected BaseService<TDto> Service { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="BaseController{TDto}"/>.
    /// </summary>
    /// <param name="service">The service for CRUD operations.</param>
    protected BaseController(BaseService<TDto> service)
    {
        Service = service;
    }

    /// <summary>
    /// Retrieves a paginated, filtered, and sorted list of DTOs.
    /// Adds pagination metadata to response headers on success.
    /// <para>
    /// Derived controllers must apply <c>[HttpGet]</c> to expose this endpoint.
    /// </para>
    /// </summary>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters from query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<ActionResult<OperationResult<PaginatedData<TDto>>>> GetAll(
        [FromQuery] FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.GetAllAsync(filterParams, cancellationToken);

        if (result.Success && result.Data is not null)
        {
            Response.Headers["X-Total-Count"] = result.Data.TotalRecords.ToString();
            Response.Headers["X-Page-Number"] = result.Data.PageNumber.ToString();
            Response.Headers["X-Page-Size"] = result.Data.PageSize.ToString();
            Response.Headers["X-Total-Pages"] = result.Data.TotalPages.ToString();
        }

        return ToActionResult(result);
    }

    /// <summary>
    /// Retrieves a single DTO by its unique identifier.
    /// <para>
    /// Derived controllers must apply <c>[HttpGet("{id}")]</c> to expose this endpoint.
    /// </para>
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<ActionResult<OperationResult<TDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.GetByIdAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates a new resource from the provided DTO.
    /// Returns 201 Created with a Location header on success.
    /// <para>
    /// Derived controllers must apply <c>[HttpPost]</c> to expose this endpoint.
    /// </para>
    /// </summary>
    /// <param name="dto">The DTO containing the data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<ActionResult<OperationResult<TDto>>> Create(
        [FromBody] TDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.AddAsync(dto, cancellationToken);

        if (result.Success && result.StatusCode == 201)
            return CreatedAtAction(nameof(GetById), new { id = Guid.Empty }, result);

        return ToActionResult(result);
    }

    /// <summary>
    /// Updates an existing resource by its unique identifier.
    /// <para>
    /// Derived controllers must apply <c>[HttpPut("{id}")]</c> to expose this endpoint.
    /// </para>
    /// </summary>
    /// <param name="id">The unique identifier of the entity to update.</param>
    /// <param name="dto">The DTO containing the updated values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<ActionResult<OperationResult<TDto>>> Update(
        Guid id,
        [FromBody] TDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.UpdateAsync(id, dto, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Deletes a resource by its unique identifier.
    /// <para>
    /// Derived controllers must apply <c>[HttpDelete("{id}")]</c> to expose this endpoint.
    /// </para>
    /// </summary>
    /// <param name="id">The unique identifier of the entity to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<ActionResult<OperationResult>> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Service.DeleteAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    #region Protected Helpers

    /// <summary>
    /// Maps a generic OperationResult to the appropriate ActionResult based on StatusCode.
    /// Protected so derived controllers can use it for custom endpoints.
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
    /// Protected so derived controllers can use it for custom endpoints.
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

    #endregion
}
