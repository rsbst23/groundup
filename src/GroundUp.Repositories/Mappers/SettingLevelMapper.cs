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

    /// <summary>Maps a create DTO to a new entity. Id and audit fields are set by the repository and database layer.</summary>
    [MapperIgnoreTarget(nameof(SettingLevel.Id))]
    [MapperIgnoreTarget(nameof(SettingLevel.Parent))]
    [MapperIgnoreTarget(nameof(SettingLevel.Children))]
    [MapperIgnoreTarget(nameof(SettingLevel.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingLevel.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingLevel.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingLevel.UpdatedBy))]
    public static partial SettingLevel ToEntity(CreateSettingLevelDto dto);

    /// <summary>Copies an update DTO onto an existing entity. Id and audit fields are not changed.</summary>
    [MapperIgnoreTarget(nameof(SettingLevel.Id))]
    [MapperIgnoreTarget(nameof(SettingLevel.Parent))]
    [MapperIgnoreTarget(nameof(SettingLevel.Children))]
    [MapperIgnoreTarget(nameof(SettingLevel.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingLevel.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingLevel.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingLevel.UpdatedBy))]
    public static partial void UpdateEntity(UpdateSettingLevelDto dto, [MappingTarget] SettingLevel entity);
}
