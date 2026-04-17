using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Data.Abstractions;

/// <summary>
/// Generic repository interface defining the standard CRUD contract.
/// All methods return <see cref="OperationResult{T}"/> or <see cref="OperationResult"/>
/// — business logic errors are communicated via result objects, never exceptions.
/// </summary>
/// <typeparam name="TDto">The DTO type exposed to the service layer.</typeparam>
public interface IBaseRepository<TDto> where TDto : class
{
    /// <summary>
    /// Retrieves a paginated, filtered, and sorted list of DTOs.
    /// </summary>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing the matching DTOs.</returns>
    Task<OperationResult<PaginatedData<TDto>>> GetAllAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single DTO by its unique identifier.
    /// Returns <see cref="OperationResult{T}.NotFound"/> if the entity does not exist.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching DTO or a NotFound result.</returns>
    Task<OperationResult<TDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new entity from the provided DTO and persists it.
    /// Returns the created DTO with any database-generated values (e.g., ID).
    /// </summary>
    /// <param name="dto">The DTO containing the data to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created DTO with a 201 status code on success.</returns>
    Task<OperationResult<TDto>> AddAsync(
        TDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing entity identified by <paramref name="id"/> with values from the DTO.
    /// Returns <see cref="OperationResult{T}.NotFound"/> if the entity does not exist.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to update.</param>
    /// <param name="dto">The DTO containing the updated values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated DTO or a NotFound result.</returns>
    Task<OperationResult<TDto>> UpdateAsync(
        Guid id,
        TDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity by its unique identifier. Performs a soft delete if the entity
    /// implements <see cref="GroundUp.Core.Entities.ISoftDeletable"/>; otherwise performs a hard delete.
    /// Returns <see cref="OperationResult.NotFound"/> if the entity does not exist.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success or NotFound result.</returns>
    Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
