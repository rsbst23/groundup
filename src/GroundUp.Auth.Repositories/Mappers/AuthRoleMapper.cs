using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for Role ↔ RoleDto conversions.
/// </summary>
[Mapper]
public static partial class AuthRoleMapper
{
    /// <summary>Maps a Role entity to a RoleDto.</summary>
    public static partial RoleDto ToDto(Role entity);

    /// <summary>Maps a RoleDto to a Role entity.</summary>
    public static partial Role ToEntity(RoleDto dto);
}
