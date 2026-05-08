using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for Policy ↔ PolicyDto conversions.
/// </summary>
[Mapper]
public static partial class AuthPolicyMapper
{
    /// <summary>Maps a Policy entity to a PolicyDto.</summary>
    public static partial PolicyDto ToDto(Policy entity);

    /// <summary>Maps a PolicyDto to a Policy entity.</summary>
    public static partial Policy ToEntity(PolicyDto dto);
}
