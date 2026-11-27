using GroundUp.Events;

namespace GroundUp.Auth.Services.Events;

/// <summary>
/// An invitation was revoked, by an administrator or because its pending member was removed. The
/// pending membership no longer exists as such; the pending user record is kept.
/// </summary>
public sealed record InvitationRevoked : BaseEvent
{
    /// <summary>The revoked invitation.</summary>
    public required Guid InvitationId { get; init; }

    /// <summary>The (pending) user the invitation was for.</summary>
    public required Guid InvitedUserId { get; init; }
}
