using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// Administration of the current tenant's roles (GU-95): list, create, rename, delete, and the
/// tenant's default role for new members. Registered by <c>AddGroundUpAuth</c> through the
/// authorization proxy, so the permissions below are enforced for HTTP and SDK callers alike.
/// Granting roles to members goes through <see cref="IUserRoleService"/>.
/// <para>
/// Roles of the System tenant (SuperAdmin) are never returned or changed: when the current tenant is
/// the System tenant the list is empty and every role id is 404.
/// </para>
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// Lists the current tenant's roles with <c>isDefault</c> and the policy and member counts, by name unless
    /// sorted. Sort: <c>name</c>, <c>roleType</c>, <c>policyCount</c>, <c>activeMemberCount</c>, <c>pendingMemberCount</c>; filter: <c>roleType</c>, <c>isSystem</c>;
    /// search: name and description. Other fields are ignored.
    /// </summary>
    /// <returns>A page of roles; 403 <c>TENANT_REQUIRED</c> without a current tenant.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<PaginatedData<TenantRoleDto>>> ListAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role of the current tenant.
    /// </summary>
    /// <returns>The role; 404 when it is not in the current tenant.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<TenantRoleDto>> GetAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a role in the current tenant. The name is trimmed.
    /// </summary>
    /// <returns>
    /// 201 with the role; 400 <c>VALIDATION_FAILED</c> (including a <c>System</c> role type);
    /// 409 <c>ROLE_NAME_TAKEN</c> when another role has the name, ignoring case.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> CreateAsync(CreateRoleDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a role or changes its description. A built-in role keeps its name exactly (not even a
    /// change of case), because code finds TenantAdmin by name; its description may change.
    /// </summary>
    /// <returns>
    /// The role; 400 <c>VALIDATION_FAILED</c>; 404; 409 <c>ROLE_NAME_TAKEN</c> or <c>ROLE_IS_BUILT_IN</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> UpdateAsync(Guid roleId, UpdateRoleDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an unused role, with its policy links. A role is in use while a member holds it, a
    /// pending or expired invitation or an active join link grants it, or it is the tenant's default
    /// role. A grant made while the delete runs makes the delete fail rather than be lost.
    /// </summary>
    /// <returns>
    /// Success; 404; 409 <c>ROLE_IS_BUILT_IN</c>; 409 <c>ROLE_IN_USE</c> with the reasons in <c>errors</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.RolesDelete)]
    Task<OperationResult> DeleteAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the role new members of the current tenant receive.
    /// </summary>
    /// <returns>The role, or <c>null</c> data when the tenant has no default role.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<TenantRoleDto?>> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a role the one new members of the current tenant receive (simple sign-up into the
    /// default tenant, and later flows that add members without a chosen role). The caller must be
    /// allowed to grant the role, and a built-in role is refused, so new members never become
    /// administrators automatically.
    /// </summary>
    /// <returns>
    /// The role; 403 when the caller may not grant it; 404; 409 <c>ROLE_IS_BUILT_IN</c>.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<TenantRoleDto>> SetDefaultAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the current tenant's default role: new members then receive no role. Succeeds when
    /// there is none.
    /// </summary>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult> ClearDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The policies attached to a role of the current tenant, by name (GU-96). The built-in
    /// TenantAdmin role has none: it passes every check without them.
    /// </summary>
    /// <returns>The policies; 404 when the role is not in the current tenant.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<List<PolicyDto>>> GetPoliciesAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a policy of the current tenant to a role (GU-96). Already attached is success (200).
    /// The caller must hold every permission of the policy unless they are TenantAdmin or SuperAdmin.
    /// Members holding the role get the policy's permissions on their next request.
    /// </summary>
    /// <returns>
    /// 201 with the link (200 when already attached); 403 <c>PERMISSION_NOT_HELD</c>; 404 for a role
    /// or policy not in the current tenant; 409 <c>ROLE_IS_BUILT_IN</c> for TenantAdmin.
    /// </returns>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult<RolePolicyDto>> AttachPolicyAsync(Guid roleId, Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches a policy from a role (GU-96). Members holding the role lose its permissions on their
    /// next request.
    /// </summary>
    /// <returns>Success; 404 when not attached; 409 <c>ROLE_IS_BUILT_IN</c> for TenantAdmin.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesWrite)]
    Task<OperationResult> DetachPolicyAsync(Guid roleId, Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a role allows (GU-96): each permission with the role's policies that grant it, or
    /// <c>AllPermissions</c> for the built-in TenantAdmin role.
    /// </summary>
    /// <returns>The permissions; 404 when the role is not in the current tenant.</returns>
    [RequiresPermission(PermissionKeysAuth.RolesRead)]
    Task<OperationResult<RolePermissionsDto>> GetPermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);
}
