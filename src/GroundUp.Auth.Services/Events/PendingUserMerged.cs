using GroundUp.Events;

namespace GroundUp.Auth.Services.Events;

/// <summary>
/// A pending user (created for an invitation) was merged into an existing user who accepted the
/// invitation. The pending user's memberships and role assignments now belong to
/// <see cref="ToUserId"/>; the pending user record is kept and marked as merged. Applications that
/// stored references to <see cref="FromUserId"/> (for example, work assigned before the invitee
/// signed in) can move them in a handler.
/// </summary>
public sealed record PendingUserMerged : BaseEvent
{
    /// <summary>The pending user that was merged.</summary>
    public required Guid FromUserId { get; init; }

    /// <summary>The user it was merged into.</summary>
    public required Guid ToUserId { get; init; }
}
