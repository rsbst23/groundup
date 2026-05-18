namespace GroundUp.Auth.Core;

/// <summary>
/// Well-known permission keys for the GroundUp auth module.
/// These are seeded on startup by <c>DefaultPermissionSeeder</c> and can be
/// referenced in <c>[RequiresPermission]</c> attributes or service-layer checks.
/// <para>
/// Convention: <c>{module}.{resource}.{action}</c> (dot-notation).
/// Actions: <c>read</c> (view), <c>write</c> (create + update), <c>delete</c> (remove).
/// </para>
/// </summary>
public static class PermissionKeysAuth
{
    // --- Users ---
    public const string UsersRead = "auth.users.read";
    public const string UsersWrite = "auth.users.write";
    public const string UsersDelete = "auth.users.delete";

    // --- Tenants ---
    public const string TenantsRead = "auth.tenants.read";
    public const string TenantsWrite = "auth.tenants.write";
    public const string TenantsDelete = "auth.tenants.delete";

    // --- Roles ---
    public const string RolesRead = "auth.roles.read";
    public const string RolesWrite = "auth.roles.write";
    public const string RolesDelete = "auth.roles.delete";
    public const string RolesAssignSystem = "auth.roles.assign-system";

    // --- Permissions ---
    public const string PermissionsRead = "auth.permissions.read";
    public const string PermissionsWrite = "auth.permissions.write";

    // --- Settings ---
    public const string SettingsRead = "settings.read";
    public const string SettingsWrite = "settings.write";
    public const string SettingsDelete = "settings.delete";

    /// <summary>
    /// All built-in permission definitions with metadata for seeding.
    /// Each tuple is (Key, Name, Description, Module).
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Name, string Description, string Module)> All = new[]
    {
        (UsersRead, "Read Users", "View user accounts and profiles", "auth"),
        (UsersWrite, "Write Users", "Create and update user accounts", "auth"),
        (UsersDelete, "Delete Users", "Deactivate or remove user accounts", "auth"),

        (TenantsRead, "Read Tenants", "View tenant information", "auth"),
        (TenantsWrite, "Write Tenants", "Create and update tenants", "auth"),
        (TenantsDelete, "Delete Tenants", "Deactivate or remove tenants", "auth"),

        (RolesRead, "Read Roles", "View roles and their policy assignments", "auth"),
        (RolesWrite, "Write Roles", "Create and update roles and policy assignments", "auth"),
        (RolesDelete, "Delete Roles", "Remove roles", "auth"),
        (RolesAssignSystem, "Assign System Roles", "Assign or revoke system-level roles (e.g., SuperAdmin). Only SuperAdmins should hold this permission.", "auth"),

        (PermissionsRead, "Read Permissions", "View permission definitions", "auth"),
        (PermissionsWrite, "Write Permissions", "Assign and revoke permissions from policies", "auth"),

        (SettingsRead, "Read Settings", "View application settings", "settings"),
        (SettingsWrite, "Write Settings", "Create and update application settings", "settings"),
        (SettingsDelete, "Delete Settings", "Remove setting definitions", "settings"),
    };
}
