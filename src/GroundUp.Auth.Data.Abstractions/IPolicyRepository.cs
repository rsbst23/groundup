using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for Policy entities. Provides standard CRUD operations
/// plus methods for managing permission assignments via the PolicyPermission junction.
/// </summary>
public interface IPolicyRepository : IBaseRepository<PolicyDto>
{
    /// <summary>
    /// Retrieves all permissions assigned to a given policy.
    /// </summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of permission DTOs assigned to the policy, or a NotFound result if the policy does not exist.</returns>
    Task<OperationResult<List<PermissionDto>>> GetPermissionsForPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a permission to a policy. Idempotent — returns success if already assigned.
    /// </summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="permissionId">The permission identifier to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success result, or NotFound if the policy or permission does not exist.</returns>
    Task<OperationResult> AssignPermissionAsync(
        Guid policyId,
        Guid permissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a permission assignment from a policy.
    /// </summary>
    /// <param name="policyId">The policy identifier.</param>
    /// <param name="permissionId">The permission identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success result, or NotFound if the assignment does not exist.</returns>
    Task<OperationResult> RemovePermissionAsync(
        Guid policyId,
        Guid permissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The current tenant's policies ordered by name, each with its permission keys and the number of
    /// the tenant's roles using it (GU-96).
    /// </summary>
    /// <param name="paging">Page number and size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of policies; 403 <c>TENANT_REQUIRED</c> without a current tenant.</returns>
    Task<OperationResult<PaginatedData<TenantPolicyDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A policy of the current tenant with its permissions and the tenant's roles that use it (GU-96).
    /// </summary>
    /// <param name="policyId">The policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The policy, or NotFound for a policy that is not in the current tenant.</returns>
    Task<OperationResult<PolicyDetailDto>> GetDetailAsync(
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the policy grant exactly <paramref name="permissionIds"/>: removes the other links and
    /// adds the missing ones, in one save (GU-96).
    /// </summary>
    /// <param name="policyId">A policy of the current tenant.</param>
    /// <param name="permissionIds">The permissions the policy grants afterwards (existing permission ids).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The links added and removed; NotFound for a policy that is not in the current tenant; 409
    /// <c>CONFLICT</c> when a concurrent change added the same link.
    /// </returns>
    Task<OperationResult<PolicyPermissionChangesDto>> ReplacePermissionsAsync(
        Guid policyId,
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default);
}
