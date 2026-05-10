namespace GroundUp.Auth.Services;

/// <summary>
/// Resolves a user's effective permissions within the current tenant context.
/// Combines tenant-scoped role permissions with system-level role permissions.
/// Results are cached per user+tenant combination.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Checks whether the user holds a specific permission in the current tenant.
    /// Resolves permissions from both tenant-scoped and system-level roles.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permissionKey">The permission key to check (e.g., "settings.read").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user holds the specified permission; otherwise false.</returns>
    Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the user holds at least one of the specified permissions in the current tenant.
    /// Resolves permissions from both tenant-scoped and system-level roles.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permissionKeys">The permission keys to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user holds at least one of the specified permissions; otherwise false.</returns>
    Task<bool> HasAnyPermissionAsync(Guid userId, IEnumerable<string> permissionKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the complete deduplicated set of permission keys the user holds in the current tenant.
    /// Includes permissions from both tenant-scoped roles and system-level roles.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A deduplicated set of permission key strings.</returns>
    Task<HashSet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the user holds a specific system-level role (case-insensitive).
    /// Only system roles (RoleType.System) are considered — tenant-scoped roles are not checked.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleName">The role name to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user holds the specified system role; otherwise false.</returns>
    Task<bool> HasSystemRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the user holds at least one of the specified system-level roles (case-insensitive).
    /// Only system roles (RoleType.System) are considered — tenant-scoped roles are not checked.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleNames">The role names to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user holds at least one of the specified system roles; otherwise false.</returns>
    Task<bool> HasAnySystemRoleAsync(Guid userId, IEnumerable<string> roleNames, CancellationToken cancellationToken = default);
}
