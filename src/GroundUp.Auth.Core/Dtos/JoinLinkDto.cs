using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A join link as shown to administrators. Never carries the token (see <see cref="CreatedJoinLinkDto"/>).
/// </summary>
/// <param name="Id">The link identifier.</param>
/// <param name="TenantId">The tenant it joins.</param>
/// <param name="RoleId">The role it grants.</param>
/// <param name="RoleName">The role's name.</param>
/// <param name="CreatedByUserId">Who created or last regenerated it.</param>
/// <param name="ExpiresAt">When it stops working (UTC); null for no expiry.</param>
/// <param name="MaxUses">How many people may join with it; null for no limit.</param>
/// <param name="UseCount">How many have joined with it.</param>
/// <param name="Status">Active, Revoked, Expired or UsedUp.</param>
/// <param name="RevokedAt">When it was revoked.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record JoinLinkDto(
    Guid Id,
    Guid TenantId,
    Guid RoleId,
    string? RoleName,
    Guid CreatedByUserId,
    DateTime? ExpiresAt,
    int? MaxUses,
    int UseCount,
    JoinLinkStatus Status,
    DateTime? RevokedAt,
    DateTime CreatedAt);
