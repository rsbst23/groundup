using GroundUp.Auth.Core;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Resolves a user's effective permissions by traversing the role hierarchy and caching results.
/// Combines tenant-scoped role permissions with system-level role permissions into a deduplicated set.
/// Short-circuits for SuperAdmin (global bypass) and TenantAdmin (tenant-scoped bypass) before cache lookup.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPolicyRepository _policyRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly AuthOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionService"/> class.
    /// </summary>
    /// <param name="userRoleRepository">Repository for user-role assignments.</param>
    /// <param name="roleRepository">Repository for roles and their policy assignments.</param>
    /// <param name="policyRepository">Repository for policies and their permission assignments.</param>
    /// <param name="tenantContext">The current tenant context.</param>
    /// <param name="cache">In-memory cache for resolved permission sets.</param>
    /// <param name="options">Auth configuration options.</param>
    public PermissionService(
        IUserRoleRepository userRoleRepository,
        IRoleRepository roleRepository,
        IPolicyRepository policyRepository,
        ITenantContext tenantContext,
        IMemoryCache cache,
        IOptions<AuthOptions> options)
    {
        _userRoleRepository = userRoleRepository;
        _roleRepository = roleRepository;
        _policyRepository = policyRepository;
        _tenantContext = tenantContext;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
    {
        // SuperAdmin and TenantAdmin bypass — short-circuit BEFORE cache lookup.
        // Never cached as a concrete permission set so newly added permissions are automatically covered.
        if (await IsSuperAdminAsync(userId, cancellationToken))
            return true;

        if (await IsTenantAdminInCurrentTenantAsync(userId, cancellationToken))
            return true;

        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(permissionKey);
    }

    /// <inheritdoc />
    public async Task<bool> HasAnyPermissionAsync(Guid userId, IEnumerable<string> permissionKeys, CancellationToken cancellationToken = default)
    {
        // SuperAdmin and TenantAdmin bypass — short-circuit BEFORE cache lookup.
        // Never cached as a concrete permission set so newly added permissions are automatically covered.
        if (await IsSuperAdminAsync(userId, cancellationToken))
            return true;

        if (await IsTenantAdminInCurrentTenantAsync(userId, cancellationToken))
            return true;

        var permissions = await GetUserPermissionsAsync(userId, cancellationToken);
        return permissionKeys.Any(permissions.Contains);
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"permissions:{userId}:{tenantId}";

        if (_cache.TryGetValue(cacheKey, out HashSet<string>? cachedPermissions) && cachedPermissions is not null)
        {
            return cachedPermissions;
        }

        var permissions = new HashSet<string>(StringComparer.Ordinal);

        // Resolve tenant-scoped roles
        var tenantRolesResult = await _userRoleRepository.GetByUserIdAsync(userId, cancellationToken);
        if (tenantRolesResult.Success && tenantRolesResult.Data is not null)
        {
            await ResolvePermissionsForRolesAsync(tenantRolesResult.Data.Select(r => r.RoleId), permissions, cancellationToken);
        }

        // Resolve system roles
        var systemRolesResult = await _userRoleRepository.GetSystemRolesForUserAsync(userId, cancellationToken);
        if (systemRolesResult.Success && systemRolesResult.Data is not null)
        {
            await ResolvePermissionsForRolesAsync(systemRolesResult.Data.Select(r => r.RoleId), permissions, cancellationToken);
        }

        // Cache the result
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.PermissionCacheTtlMinutes)
        };
        _cache.Set(cacheKey, permissions, cacheOptions);

        return permissions;
    }

    /// <inheritdoc />
    public async Task<bool> HasSystemRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var systemRolesResult = await _userRoleRepository.GetSystemRolesForUserAsync(userId, cancellationToken);
        if (!systemRolesResult.Success || systemRolesResult.Data is null)
        {
            return false;
        }

        return systemRolesResult.Data.Any(r =>
            string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<bool> HasAnySystemRoleAsync(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default)
    {
        var systemRolesResult = await _userRoleRepository.GetSystemRolesForUserAsync(userId, cancellationToken);
        if (!systemRolesResult.Success || systemRolesResult.Data is null)
        {
            return false;
        }

        var roleNameSet = new HashSet<string>(roleNames, StringComparer.OrdinalIgnoreCase);
        return systemRolesResult.Data.Any(r => r.RoleName is not null && roleNameSet.Contains(r.RoleName));
    }

    /// <summary>
    /// Determines whether the user holds the SuperAdmin system role.
    /// SuperAdmin bypasses all permission checks everywhere (global).
    /// </summary>
    private async Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var systemRolesResult = await _userRoleRepository.GetSystemRolesForUserAsync(userId, cancellationToken);
        if (systemRolesResult is null || !systemRolesResult.Success || systemRolesResult.Data is null)
            return false;

        return systemRolesResult.Data.Any(r =>
            string.Equals(r.RoleName, AuthRoleNames.SuperAdmin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the user holds the TenantAdmin role in the current tenant.
    /// Uses the tenant-scoped query (<see cref="IUserRoleRepository.GetByUserIdForTenantAsync"/>)
    /// so the check never leaks across tenants. Skipped when no tenant context is available
    /// (TenantId == Guid.Empty, e.g., during pending tenant selection).
    /// </summary>
    private async Task<bool> IsTenantAdminInCurrentTenantAsync(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            return false;

        var rolesResult = await _userRoleRepository.GetByUserIdForTenantAsync(userId, tenantId, cancellationToken);
        if (rolesResult is null || !rolesResult.Success || rolesResult.Data is null)
            return false;

        return rolesResult.Data.Any(r =>
            string.Equals(r.RoleName, AuthRoleNames.TenantAdmin, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ResolvePermissionsForRolesAsync(
        IEnumerable<Guid> roleIds,
        HashSet<string> permissions,
        CancellationToken cancellationToken)
    {
        foreach (var roleId in roleIds)
        {
            var policiesResult = await _roleRepository.GetPoliciesForRoleAsync(roleId, cancellationToken);
            if (!policiesResult.Success || policiesResult.Data is null)
            {
                continue;
            }

            foreach (var policy in policiesResult.Data)
            {
                var permissionsResult = await _policyRepository.GetPermissionsForPolicyAsync(policy.Id, cancellationToken);
                if (!permissionsResult.Success || permissionsResult.Data is null)
                {
                    continue;
                }

                foreach (var permission in permissionsResult.Data)
                {
                    permissions.Add(permission.Key);
                }
            }
        }
    }
}
