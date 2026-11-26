using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// The three kinds of role that the grant rules distinguish (GU-68 Requirement 1).
/// </summary>
internal enum RoleKind
{
    /// <summary>A system role in the System tenant (SuperAdmin).</summary>
    SuperAdmin,

    /// <summary>The built-in per-tenant TenantAdmin role.</summary>
    TenantAdmin,

    /// <summary>Any other role, defined by the application or a tenant.</summary>
    Other
}

/// <summary>
/// Classifies roles from existing data, with no stored role kind.
/// </summary>
internal static class RoleKinds
{
    /// <summary>
    /// SuperAdmin: <see cref="RoleType.System"/> in the System tenant. Any system role there is
    /// treated as SuperAdmin-level, which is the stricter choice. TenantAdmin: the built-in role
    /// (<c>IsSystem</c>) named TenantAdmin. Everything else: Other.
    /// </summary>
    public static RoleKind Classify(RoleDto role)
    {
        if (role.RoleType == RoleType.System && role.TenantId == AuthRoleNames.SystemTenantId)
        {
            return RoleKind.SuperAdmin;
        }

        if (role.IsSystem && string.Equals(role.Name, AuthRoleNames.TenantAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return RoleKind.TenantAdmin;
        }

        return RoleKind.Other;
    }
}
