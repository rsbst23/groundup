namespace GroundUp.Auth.Services.Roles;

/// <summary>
/// The kind of role change being checked.
/// </summary>
public enum RoleChange
{
    /// <summary>Granting a role.</summary>
    Assign = 0,

    /// <summary>Removing a role.</summary>
    Remove = 1
}
