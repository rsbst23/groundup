using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;
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
    /// A page of the current tenant's roles with <c>isDefault</c> and <c>policyCount</c> (GU-97), by name
    /// unless sorted. Sort: <c>name</c>, <c>roleType</c>, <c>policyCount</c>; filter: <c>roleType</c>,
    /// <c>isSystem</c>; search: name and description. Other fields are ignored.
    /// </summary>
    /// <param name="filterParams">Paging, sorting, filters and search.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page; 403 <c>TENANT_REQUIRED</c> without a current tenant.</returns>
    Task<OperationResult<PaginatedData<TenantRoleDto>>> ListAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>How many policies are attached to the role (in the current tenant).</summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count (0 for an unknown role).</returns>
    Task<OperationResult<int>> CountPoliciesAsync(Guid roleId, CancellationToken cancellationToken = default);

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
    /// <returns>
    /// 201 with the new link, 200 with the existing one, or NotFound if the role or policy does not
    /// exist in the current tenant.
    /// </returns>
    Task<OperationResult<RolePolicyDto>> AssignPolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a policy assignment from a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="policyId">The policy identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The removed link, or NotFound if the assignment does not exist.</returns>
    Task<OperationResult<RolePolicyDto>> RemovePolicyAsync(
        Guid roleId,
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a role by ID in any tenant. System-level bypass: the role service needs SuperAdmin,
    /// which lives in the System tenant, and checks the role's tenant itself.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role, or NotFound.</returns>
    Task<OperationResult<RoleDto>> FindByIdAnyTenantAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a role by name (case-insensitive) in <paramref name="tenantId"/>. System-level bypass:
    /// targets the tenant explicitly, for sign-in callbacks that run before a tenant is selected.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="name">The role name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The role, or NotFound.</returns>
    Task<OperationResult<RoleDto>> GetByNameAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What uses a role of the current tenant (GU-95): its holders, pending invitations, active join
    /// links, and whether it is the tenant's default role. Checked before a delete.
    /// </summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The usage, or NotFound for a role that is not in the current tenant.</returns>
    Task<OperationResult<RoleUsageDto>> GetUsageAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The permissions a role grants, one row per permission and policy, ordered by key (GU-96).
    /// System-level read by role id: role grants are also checked in sign-in callbacks, before a
    /// tenant is selected, so the caller checks the role's tenant itself.
    /// </summary>
    /// <param name="roleId">The role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The grants (empty for a role without policies or an unknown id).</returns>
    Task<OperationResult<List<RoleGrantDto>>> GetPermissionGrantsAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
}
