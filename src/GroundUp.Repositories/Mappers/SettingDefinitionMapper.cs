using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for <see cref="SettingDefinition"/> and its options.
/// The definition's <see cref="SettingDefinition.AllowedLevels"/> and <see cref="SettingDefinition.Options"/>
/// must be loaded; an unloaded collection maps to an empty list.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SettingDefinitionMapper
{
    /// <summary>Maps a SettingDefinition entity (with allowed levels and options) to a SettingDefinitionDto.</summary>
    [MapProperty(nameof(SettingDefinition.AllowedLevels), nameof(SettingDefinitionDto.AllowedLevelIds))]
    public static partial SettingDefinitionDto ToDto(SettingDefinition entity);

    /// <summary>Maps a SettingOption entity to a SettingOptionDto.</summary>
    public static partial SettingOptionDto ToDto(SettingOption entity);

    private static IReadOnlyList<Guid> MapAllowedLevelIds(ICollection<SettingDefinitionLevel> allowedLevels)
        => allowedLevels.Select(l => l.SettingLevelId).ToList();

    private static IReadOnlyList<SettingOptionDto> MapOptions(ICollection<SettingOption> options)
        => options
            .OrderBy(o => o.DisplayOrder)
            .ThenBy(o => o.Value, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
}
