using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for Permission ↔ PermissionDto conversions.
/// </summary>
[Mapper]
public static partial class AuthPermissionMapper
{
    /// <summary>Maps a Permission entity to a PermissionDto.</summary>
    public static partial PermissionDto ToDto(Permission entity);

    /// <summary>Maps a PermissionDto to a Permission entity.</summary>
    public static partial Permission ToEntity(PermissionDto dto);
}
