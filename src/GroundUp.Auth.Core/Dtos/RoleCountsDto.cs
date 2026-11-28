namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// The counts shown with a role (GU-97): its policies and the people holding it, by membership status.
/// Computed on read; nothing is stored.
/// </summary>
/// <param name="PolicyCount">The policies attached to the role.</param>
/// <param name="ActiveMemberCount">The distinct active members holding the role (any source).</param>
/// <param name="PendingMemberCount">The distinct pending members holding it (open invitations to the role).</param>
public sealed record RoleCountsDto(int PolicyCount, int ActiveMemberCount, int PendingMemberCount);
