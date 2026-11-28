namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// What uses a role, checked before it is deleted (GU-95).
/// </summary>
/// <param name="Holders">Role assignments in the tenant (any membership status, any source).</param>
/// <param name="PendingInvitations">Invitations for the role that are Pending or Expired (not yet revoked or accepted).</param>
/// <param name="ActiveJoinLinks">Join links for the role that are Active.</param>
/// <param name="IsTenantDefault">Whether the role is the tenant's default role.</param>
public sealed record RoleUsageDto(int Holders, int PendingInvitations, int ActiveJoinLinks, bool IsTenantDefault)
{
    /// <summary>True when nothing uses the role.</summary>
    public bool IsUnused => Holders == 0 && PendingInvitations == 0 && ActiveJoinLinks == 0 && !IsTenantDefault;
}
