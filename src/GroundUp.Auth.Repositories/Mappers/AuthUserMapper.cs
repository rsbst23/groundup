using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for User ↔ UserDto conversions.
/// </summary>
[Mapper]
public static partial class AuthUserMapper
{
    /// <summary>Maps a User entity to a UserDto.</summary>
    public static partial UserDto ToDto(User entity);

    /// <summary>Maps a UserDto to a User entity.</summary>
    public static partial User ToEntity(UserDto dto);
}
