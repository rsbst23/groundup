using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A member of a tenant, as listed for administrators.
/// </summary>
/// <param name="UserId">The user identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="Status">Active, or Pending for an invited person who has not accepted yet.</param>
/// <param name="Roles">The member's roles in the tenant, each with the sources it is held from.</param>
public sealed record TenantMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    MembershipStatus Status,
    IReadOnlyList<MemberRoleDto> Roles);

/// <summary>
/// A role a member holds, and why.
/// </summary>
/// <param name="RoleId">The role identifier.</param>
/// <param name="RoleName">The role name.</param>
/// <param name="Sources">Every source the role is held from (one assignment each).</param>
public sealed record MemberRoleDto(
    Guid RoleId,
    string RoleName,
    IReadOnlyList<RoleAssignmentSource> Sources);
