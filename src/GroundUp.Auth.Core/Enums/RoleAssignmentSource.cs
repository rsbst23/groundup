namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Why a role assignment exists. A user can hold the same role from several sources; they have the
/// role while any assignment exists. Automatic processes remove only assignments with their own source.
/// </summary>
public enum RoleAssignmentSource
{
    /// <summary>Granted by an administrator.</summary>
    Manual = 0,

    /// <summary>Granted by an invitation.</summary>
    Invitation = 1,

    /// <summary>Granted by a join link.</summary>
    JoinLink = 2,

    /// <summary>Granted as the tenant's default role (<c>Tenant.DefaultRoleId</c>) when joining.</summary>
    DefaultRole = 3,

    /// <summary>Granted by the system while provisioning (organization creation, SuperAdmin bootstrap).</summary>
    Provisioning = 4
}
