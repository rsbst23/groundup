using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for Tenant entities. Provides standard CRUD operations
/// plus custom lookup methods by slug and hierarchical child tenant queries.
/// </summary>
public interface ITenantRepository : IBaseRepository<TenantDto>
{
    /// <summary>
    /// Retrieves a tenant by its URL-friendly slug identifier.
    /// </summary>
    /// <param name="slug">The tenant slug to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching tenant DTO or a NotFound result.</returns>
    Task<OperationResult<TenantDto>> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all child tenants of a given parent tenant with pagination and filtering.
    /// </summary>
    /// <param name="parentTenantId">The parent tenant identifier.</param>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing the child tenant DTOs.</returns>
    Task<OperationResult<PaginatedData<TenantDto>>> GetChildTenantsAsync(
        Guid parentTenantId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple tenants by their IDs in a single query, bypassing the
    /// ambient tenant-context visibility filter. Soft-deleted tenants are still excluded.
    /// </summary>
    /// <remarks>
    /// <b>SECURITY:</b> This method bypasses tenant visibility enforcement. Callers MUST
    /// validate that the requested tenant IDs are authorized for the current user before
    /// invoking this method. The only intended consumer is <c>AuthSessionService.SetTenantAsync</c>,
    /// which validates membership via <c>IUserTenantRepository.GetAllMembershipsForUserAsync</c>
    /// before calling this method with the user's own membership tenant IDs.
    /// </remarks>
    /// <param name="tenantIds">The tenant identifiers to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of tenant DTOs matching the given IDs (excluding soft-deleted), in no particular order.</returns>
    Task<OperationResult<List<TenantDto>>> GetByIdsBypassFilterAsync(
        IEnumerable<Guid> tenantIds,
        CancellationToken cancellationToken = default);
}
