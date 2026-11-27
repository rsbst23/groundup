namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// The state of a join link. Only <see cref="Active"/> and <see cref="Revoked"/> are stored; an
/// Active link is reported as <see cref="Expired"/> past its expiry, or <see cref="UsedUp"/> once
/// it reaches its maximum number of uses.
/// </summary>
public enum JoinLinkStatus
{
    /// <summary>Can be used.</summary>
    Active = 0,

    /// <summary>Revoked by an administrator.</summary>
    Revoked = 1,

    /// <summary>Reported only: past its expiry.</summary>
    Expired = 2,

    /// <summary>Reported only: its maximum number of uses is reached.</summary>
    UsedUp = 3
}
