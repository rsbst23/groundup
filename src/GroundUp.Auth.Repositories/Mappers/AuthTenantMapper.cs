using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for Tenant ↔ TenantDto conversions.
/// </summary>
[Mapper]
public static partial class AuthTenantMapper
{
    /// <summary>Maps a Tenant entity to a TenantDto.</summary>
    public static partial TenantDto ToDto(Tenant entity);

    /// <summary>Maps a TenantDto to a Tenant entity.</summary>
    public static partial Tenant ToEntity(TenantDto dto);
}
