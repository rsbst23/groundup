namespace GroundUp.Auth.Core;

/// <summary>
/// Well-known system role names for the GroundUp auth module.
/// System roles are seeded under the well-known System tenant
/// (<see cref="SystemTenantId"/>) and have <c>RoleType = System</c>,
/// meaning they are framework-defined and immutable.
/// </summary>
public static class AuthRoleNames
{
    /// <summary>
    /// The well-known tenant ID used for system-level roles and policies.
    /// This tenant is created by the auth seeders on first startup.
    /// </summary>
    public static readonly Guid SystemTenantId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Full system access. Bypasses all permission checks.
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>
    /// Tenant-scoped full access. Bypasses all permission checks within the tenant.
    /// This is NOT a global System role — it is created per-tenant with <c>IsSystem=true</c>
    /// and scoped to the owning tenant's <c>TenantId</c>.
    /// </summary>
    public const string TenantAdmin = "TenantAdmin";
}
