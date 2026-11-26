namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// What the holder of an invitation link may see about it before signing in.
/// </summary>
/// <param name="Usable">True when the invitation can still be accepted.</param>
/// <param name="Reason">
/// Why it cannot be used: <c>not_found</c>, <c>revoked</c>, <c>expired</c> or <c>accepted</c>. Null when usable.
/// </param>
/// <param name="OrganizationName">The inviting organization, only when usable.</param>
/// <param name="RoleName">The offered role, only when usable.</param>
public sealed record InvitationLookupDto(bool Usable, string? Reason, string? OrganizationName, string? RoleName);
