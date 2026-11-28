namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// One permission a role grants, with the policies that grant it (GU-96).
/// </summary>
/// <param name="Key">The permission key.</param>
/// <param name="Name">The permission's display name.</param>
/// <param name="Module">The module that defines the permission.</param>
/// <param name="GrantedBy">The role's policies that grant it, sorted by name.</param>
public sealed record RolePermissionDto(string Key, string Name, string Module, List<EntityRefDto> GrantedBy);