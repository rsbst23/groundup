using GroundUp.Events;

namespace GroundUp.Auth.Services.Events;

/// <summary>
/// An invitation was accepted: the invitee signed in through its link and their membership is
/// Active (or they were already a member, see <see cref="AlreadyMember"/>).
/// </summary>
public sealed record InvitationAccepted : BaseEvent
{
    /// <summary>The accepted invitation.</summary>
    public required Guid InvitationId { get; init; }

    /// <summary>True when the invitee was already an active member; nothing changed but the invitation.</summary>
    public bool AlreadyMember { get; init; }
}
