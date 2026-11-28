namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// What a role allows (GU-96).
/// </summary>
/// <param name="RoleId">The role id.</param>
/// <param name="AllPermissions">
/// True for the built-in TenantAdmin role, which passes every permission check without policies.
/// <paramref name="Permissions"/> is then empty.
/// </param>
/// <param name="Permissions">The permissions granted through the role's policies, sorted by key.</param>
public sealed record RolePermissionsDto(Guid RoleId, bool AllPermissions, List<RolePermissionDto> Permissions);