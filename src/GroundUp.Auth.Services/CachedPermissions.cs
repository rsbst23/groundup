namespace GroundUp.Auth.Services;

/// <summary>
/// A user's resolved permission keys in a tenant, as cached by <see cref="PermissionService"/> under
/// <see cref="PermissionCacheKeys.For"/>, with the tenant's <see cref="PermissionCacheVersions"/>
/// version read before the keys were loaded (GU-96). An entry with an older version is reloaded.
/// </summary>
/// <param name="Version">The tenant's version when loading started.</param>
/// <param name="Keys">The permission keys.</param>
public sealed record CachedPermissions(long Version, HashSet<string> Keys);
