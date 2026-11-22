namespace GroundUp.Auth.Services;

/// <summary>
/// The one place that builds the cache key for a user's resolved permissions in a tenant.
/// Used by <see cref="PermissionService"/> (which caches) and by every component that evicts,
/// so the formats cannot drift apart.
/// </summary>
public static class PermissionCacheKeys
{
    /// <summary>
    /// Returns the permission cache key for <paramref name="userId"/> in <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="tenantId">The tenant (<see cref="Guid.Empty"/> for no tenant).</param>
    /// <returns>The cache key.</returns>
    public static string For(Guid userId, Guid tenantId) => $"permissions:{userId}:{tenantId}";
}
