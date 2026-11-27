namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// What the holder of a join link may see about it before signing in.
/// </summary>
/// <param name="Usable">True when the link can still be used.</param>
/// <param name="Reason">
/// Why it cannot be used: <c>not_found</c>, <c>revoked</c>, <c>expired</c> or <c>used_up</c>. Null when usable.
/// </param>
/// <param name="OrganizationName">The organization, only when usable.</param>
/// <param name="RoleName">The role, only when usable.</param>
public sealed record JoinLinkLookupDto(bool Usable, string? Reason, string? OrganizationName, string? RoleName);
