using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Auth.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for AuthFlowState ↔ AuthFlowStateDto conversions.
/// </summary>
[Mapper]
public static partial class AuthFlowStateMapper
{
    /// <summary>Maps an AuthFlowState entity to an AuthFlowStateDto.</summary>
    public static partial AuthFlowStateDto ToDto(AuthFlowState entity);

    /// <summary>Maps an AuthFlowStateDto to an AuthFlowState entity.</summary>
    public static partial AuthFlowState ToEntity(AuthFlowStateDto dto);
}
