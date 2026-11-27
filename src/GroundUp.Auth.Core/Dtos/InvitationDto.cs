using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// An invitation as shown to administrators. Never carries the token.
/// </summary>
/// <param name="Id">The invitation identifier.</param>
/// <param name="TenantId">The tenant the invitation is for.</param>
/// <param name="Email">The invited email address.</param>
/// <param name="RoleId">The role it grants.</param>
/// <param name="RoleName">The role's name.</param>
/// <param name="UserId">The (pending) user whose membership it created.</param>
/// <param name="CreatedByUserId">Who created or last resent it.</param>
/// <param name="ExpiresAt">When it stops working (UTC).</param>
/// <param name="Status">Pending, Accepted, Revoked, or Expired (a Pending invitation past its expiry).</param>
/// <param name="AcceptedByUserId">Who accepted it.</param>
/// <param name="AcceptedAt">When it was accepted.</param>
/// <param name="RevokedAt">When it was revoked.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record InvitationDto(
    Guid Id,
    Guid TenantId,
    string Email,
    Guid RoleId,
    string? RoleName,
    Guid UserId,
    Guid CreatedByUserId,
    DateTime ExpiresAt,
    InvitationStatus Status,
    Guid? AcceptedByUserId,
    DateTime? AcceptedAt,
    DateTime? RevokedAt,
    DateTime CreatedAt);
