using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for UserTenant ↔ UserTenantDto conversions.
/// </summary>
[Mapper]
public static partial class AuthUserTenantMapper
{
    /// <summary>Maps a UserTenant entity to a UserTenantDto.</summary>
    public static partial UserTenantDto ToDto(UserTenant entity);

    /// <summary>Maps a UserTenantDto to a UserTenant entity.</summary>
    public static partial UserTenant ToEntity(UserTenantDto dto);
}
