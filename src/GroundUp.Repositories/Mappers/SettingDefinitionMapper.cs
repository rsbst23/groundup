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

    /// <summary>
    /// Maps a create DTO's scalar fields to a new entity. Options and allowed levels are added by the
    /// repository; id and audit fields are set by the repository and database layer.
    /// </summary>
    [MapperIgnoreTarget(nameof(SettingDefinition.Id))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Group))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Options))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Values))]
    [MapperIgnoreTarget(nameof(SettingDefinition.AllowedLevels))]
    [MapperIgnoreTarget(nameof(SettingDefinition.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingDefinition.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingDefinition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingDefinition.UpdatedBy))]
    [MapperIgnoreSource(nameof(CreateSettingDefinitionDto.Options))]
    [MapperIgnoreSource(nameof(CreateSettingDefinitionDto.AllowedLevelIds))]
    public static partial SettingDefinition ToEntity(CreateSettingDefinitionDto dto);

    /// <summary>
    /// Copies an update DTO's scalar fields onto an existing entity. Options and allowed levels are
    /// replaced by the repository; id and audit fields are not changed.
    /// </summary>
    [MapperIgnoreTarget(nameof(SettingDefinition.Id))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Group))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Options))]
    [MapperIgnoreTarget(nameof(SettingDefinition.Values))]
    [MapperIgnoreTarget(nameof(SettingDefinition.AllowedLevels))]
    [MapperIgnoreTarget(nameof(SettingDefinition.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingDefinition.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingDefinition.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingDefinition.UpdatedBy))]
    [MapperIgnoreSource(nameof(UpdateSettingDefinitionDto.Options))]
    [MapperIgnoreSource(nameof(UpdateSettingDefinitionDto.AllowedLevelIds))]
    public static partial void UpdateEntity(UpdateSettingDefinitionDto dto, [MappingTarget] SettingDefinition entity);

    /// <summary>Maps a create-option DTO to a new option entity of the given definition.</summary>
    public static SettingOption ToEntity(CreateSettingOptionDto dto, Guid settingDefinitionId)
    {
        var option = ToOptionEntity(dto);
        option.SettingDefinitionId = settingDefinitionId;
        return option;
    }

    [MapperIgnoreTarget(nameof(SettingOption.Id))]
    [MapperIgnoreTarget(nameof(SettingOption.SettingDefinitionId))]
    [MapperIgnoreTarget(nameof(SettingOption.SettingDefinition))]
    private static partial SettingOption ToOptionEntity(CreateSettingOptionDto dto);

    private static IReadOnlyList<Guid> MapAllowedLevelIds(ICollection<SettingDefinitionLevel> allowedLevels)
        => allowedLevels.Select(l => l.SettingLevelId).ToList();

    private static IReadOnlyList<SettingOptionDto> MapOptions(ICollection<SettingOption> options)
        => options
            .OrderBy(o => o.DisplayOrder)
            .ThenBy(o => o.Value, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
}
