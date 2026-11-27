namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to invite someone to the current tenant.
/// </summary>
/// <param name="Email">The invitee's email address (required).</param>
/// <param name="RoleId">The role to grant (required, a role in the current tenant).</param>
/// <param name="ExpiresAt">
/// Optional expiry (UTC). It can only shorten the lifetime set by <c>auth.invitations.lifetime-hours</c>.
/// </param>
public sealed record CreateInvitationDto(string? Email, Guid RoleId, DateTime? ExpiresAt = null);
