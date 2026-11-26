namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// The state of an invitation. <see cref="Expired"/> is never stored: an invitation is reported as
/// Expired once its expiry passes while it is still Pending, without a background job.
/// </summary>
public enum InvitationStatus
{
    /// <summary>Sent and not yet accepted or revoked.</summary>
    Pending = 0,

    /// <summary>Accepted by the invitee (or closed because they were already a member).</summary>
    Accepted = 1,

    /// <summary>Revoked by an administrator.</summary>
    Revoked = 2,

    /// <summary>Reported only: still Pending in storage, but past its expiry.</summary>
    Expired = 3
}
