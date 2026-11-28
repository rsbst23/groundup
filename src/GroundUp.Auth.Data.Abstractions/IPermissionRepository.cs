using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for Permission entities. Provides standard CRUD operations
/// plus custom lookup methods by permission key and module.
/// </summary>
public interface IPermissionRepository : IBaseRepository<PermissionDto>
{
    /// <summary>
    /// Retrieves a permission by its unique programmatic key.
    /// </summary>
    /// <param name="key">The permission key (e.g., "settings.read").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching permission DTO or a NotFound result.</returns>
    Task<OperationResult<PermissionDto>> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all permissions belonging to a given module with pagination and filtering.
    /// </summary>
    /// <param name="module">The module name to filter by.</param>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing the matching permission DTOs.</returns>
    Task<OperationResult<PaginatedData<PermissionDto>>> GetByModuleAsync(
        string module,
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every permission in the catalogue, ordered by module then key (GU-96). The catalogue is small
    /// (one row per permission key), so it is not paged.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<PermissionDto>>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The permissions with these exact keys (GU-96). Keys that match no permission are left out, so
    /// the caller can tell which keys are unknown.
    /// </summary>
    /// <param name="keys">The keys to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<List<PermissionDto>>> GetByKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default);
}
