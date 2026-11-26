using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Read-only representation of a user-tenant membership for API responses.
/// </summary>
/// <param name="Id">The unique identifier of the user-tenant record.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="ExternalUserId">
/// The identity provider user identifier specific to this tenant. Null while the user is pending.
/// </param>
/// <param name="Status">The membership state.</param>
public record UserTenantDto(
    Guid Id,
    Guid UserId,
    Guid TenantId,
    string? ExternalUserId,
    MembershipStatus Status)
{
    /// <summary>
    /// True only for an <see cref="MembershipStatus.Active"/> membership. Pending and Inactive
    /// memberships give no access.
    /// </summary>
    public bool IsActive => Status == MembershipStatus.Active;
}
