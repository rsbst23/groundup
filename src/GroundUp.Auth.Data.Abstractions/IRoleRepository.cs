using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;

namespace GroundUp.Auth.Data.Abstractions;

/// <summary>
/// Repository interface for Role entities. Provides standard CRUD operations
/// plus methods for managing policy assignments via the RolePolicy junction.
/// </summary>
public interface IRoleRepository : IBaseRepository<RoleDto>
{
    /// <summary>
    /// Retrieves all policies assigned to a given role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of policy DTOs assigned to the role, or a NotFound result if the role does not exist.</returns>
    Task<OperationResult<List<PolicyDto>>> GetPoliciesForRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a policy to a role. Idempotent — returns success if already assigned.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="policyId">The policy identifier to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success result, or NotFound if the role or policy does not exist.</returns>
    Task<OperationResult> AssignPolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a policy assignment from a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="policyId">The policy identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success result, or NotFound if the assignment does not exist.</returns>
    Task<OperationResult> RemovePolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default);
}
