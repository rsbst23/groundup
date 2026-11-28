using System.Collections.Concurrent;

namespace GroundUp.Auth.Services;

/// <summary>
/// A version number per tenant for cached permission sets (GU-96). A change that can alter what many
/// users of a tenant hold (a policy's permissions, a role's policies, a policy delete) bumps the
/// tenant's version, and <see cref="PermissionService"/> reloads any cached set built under an older
/// version. So every user of the tenant gets the change on their next request, without evicting them
/// one by one.
/// <para>
/// Registered as a singleton by <c>AddGroundUpAuth</c>. Like the cache it versions, it is per process:
/// other instances see the change when their cached sets expire (until a distributed cache, GU-47).
/// </para>
/// </summary>
public sealed class PermissionCacheVersions
{
    private readonly ConcurrentDictionary<Guid, long> _versions = new();

    /// <summary>
    /// The tenant's current version (0 until the first bump).
    /// </summary>
    /// <param name="tenantId">The tenant (<see cref="Guid.Empty"/> for no tenant).</param>
    public long Current(Guid tenantId) => _versions.TryGetValue(tenantId, out var version) ? version : 0;

    /// <summary>
    /// Makes every cached permission set of the tenant stale. Call after the change is saved.
    /// </summary>
    /// <param name="tenantId">The tenant whose permissions changed.</param>
    /// <returns>The new version.</returns>
    public long Bump(Guid tenantId) => _versions.AddOrUpdate(tenantId, 1, (_, version) => version + 1);
}
