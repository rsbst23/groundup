namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// The state of a user's membership in a tenant.
/// </summary>
public enum MembershipStatus
{
    /// <summary>The user is a member and has access. Default for new memberships.</summary>
    Active = 0,

    /// <summary>The user left or was removed. The row is kept so a rejoin reuses it.</summary>
    Inactive = 1,

    /// <summary>
    /// The user was invited but has not accepted yet. Roles may already be assigned, but they have
    /// no effect and the membership counts as inactive everywhere until it becomes Active.
    /// </summary>
    Pending = 2
}
