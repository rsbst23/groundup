using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for <see cref="SettingGroup"/> conversions.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SettingGroupMapper
{
    /// <summary>Maps a SettingGroup entity to a SettingGroupDto.</summary>
    public static partial SettingGroupDto ToDto(SettingGroup entity);
}
