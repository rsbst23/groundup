using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Policies;

/// <summary>
/// The permission catalogue and the current tenant's policies (GU-96). A policy is a named set of
/// permissions; roles grant permissions through the policies attached to them
/// (<see cref="Roles.IRoleService"/>). Registered by <c>AddGroundUpAuth</c> through the authorization
/// proxy, so the permissions below are enforced for HTTP and SDK callers alike.
/// <para>
/// A caller who is neither SuperAdmin nor TenantAdmin can only add permissions they hold themselves
/// (403 <c>PERMISSION_NOT_HELD</c>). Every change that alters what members hold makes the tenant's
/// cached permissions stale on this instance, so members see it on their next request. The System
/// tenant's policies are not reachable: there the list is empty and every policy id is 404.
/// </para>
/// </summary>
public interface IPolicyService
{
    /// <summary>
    /// Every permission a tenant policy can grant, grouped by module. System-only keys
    /// (<see cref="PermissionKeysAuth.SystemOnly"/>) are left out.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<List<PermissionModuleDto>>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The current tenant's policies ordered by name, each with its permission keys and how many roles use it.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<PaginatedData<TenantPolicyDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A policy with its permissions and the roles using it.
    /// </summary>
    /// <returns>The policy; 404 when it is not in the current tenant.</returns>
    [RequiresPermission(PermissionKeysAuth.PermissionsRead)]
    Task<OperationResult<PolicyDetailDto>> GetAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a policy, optionally with its permissions. The name is trimmed.
    /// </summary>
    /// <returns>
    /// 201 with the policy; 400 <c>VALIDATION_FAILED</c> (including unknown and system-only keys);
    /// 403 <c>PERMISSION_NOT_HELD</c>; 409 <c>POLICY_NAME_TAKEN</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> CreateAsync(CreatePolicyDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a policy or changes its description.
    /// </summary>
    /// <returns>The policy; 400; 404; 409 <c>POLICY_NAME_TAKEN</c>.</returns>
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> UpdateAsync(Guid policyId, UpdatePolicyDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the policy's permissions with the given keys. Only keys being added are checked
    /// against what the caller holds; keys kept or removed are not.
    /// </summary>
    /// <returns>
    /// The policy; 400 (unknown or system-only keys); 403 <c>PERMISSION_NOT_HELD</c>; 404; 409 when
    /// another change to the same policy ran at the same time.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult<PolicyDetailDto>> SetPermissionsAsync(
        Guid policyId,
        SetPolicyPermissionsDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a policy that no role uses, with its permission links.
    /// </summary>
    /// <returns>Success; 404; 409 <c>POLICY_IN_USE</c> with the roles in <c>errors</c>.</returns>
    [RequiresPermission(PermissionKeysAuth.PermissionsWrite)]
    Task<OperationResult> DeleteAsync(Guid policyId, CancellationToken cancellationToken = default);
}
