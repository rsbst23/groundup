using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for UserRole ↔ UserRoleDto conversions.
/// </summary>
[Mapper]
public static partial class AuthUserRoleMapper
{
    /// <summary>Maps a UserRole entity to a UserRoleDto.</summary>
    [MapperIgnoreTarget(nameof(UserRoleDto.RoleName))]
    public static partial UserRoleDto ToDto(UserRole entity);

    /// <summary>Maps a UserRoleDto to a UserRole entity.</summary>
    [MapperIgnoreSource(nameof(UserRoleDto.RoleName))]
    public static partial UserRole ToEntity(UserRoleDto dto);
}
