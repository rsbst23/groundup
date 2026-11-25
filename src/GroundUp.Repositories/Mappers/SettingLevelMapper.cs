using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for <see cref="SettingLevel"/> conversions.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SettingLevelMapper
{
    /// <summary>Maps a SettingLevel entity to a SettingLevelDto.</summary>
    public static partial SettingLevelDto ToDto(SettingLevel entity);
}
