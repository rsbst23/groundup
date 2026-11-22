namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// What <c>IIdentityBootstrapRepository.EnsureSuperAdminAsync</c> writes: the user, their membership in
/// <paramref name="TenantId"/>, and the assignment of role <paramref name="RoleId"/>.
/// </summary>
/// <param name="Email">The user's email (the existence check for the user row).</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="ExternalUserId">The identity provider's subject for the user.</param>
/// <param name="TenantId">The tenant of the membership and role assignment (the System tenant).</param>
/// <param name="RoleId">The SuperAdmin role.</param>
public sealed record SuperAdminRecord(
    string Email,
    string DisplayName,
    string ExternalUserId,
    Guid TenantId,
    Guid RoleId);
