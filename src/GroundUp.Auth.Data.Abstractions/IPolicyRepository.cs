using GroundUp.Auth.Core.Dtos;
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
}
