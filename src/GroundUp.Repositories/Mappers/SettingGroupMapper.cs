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

    /// <summary>Maps a create DTO to a new entity. Id and audit fields are set by the repository and database layer.</summary>
    [MapperIgnoreTarget(nameof(SettingGroup.Id))]
    [MapperIgnoreTarget(nameof(SettingGroup.Settings))]
    [MapperIgnoreTarget(nameof(SettingGroup.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingGroup.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingGroup.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingGroup.UpdatedBy))]
    public static partial SettingGroup ToEntity(CreateSettingGroupDto dto);

    /// <summary>Copies an update DTO onto an existing entity. Id and audit fields are not changed.</summary>
    [MapperIgnoreTarget(nameof(SettingGroup.Id))]
    [MapperIgnoreTarget(nameof(SettingGroup.Settings))]
    [MapperIgnoreTarget(nameof(SettingGroup.CreatedAt))]
    [MapperIgnoreTarget(nameof(SettingGroup.CreatedBy))]
    [MapperIgnoreTarget(nameof(SettingGroup.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SettingGroup.UpdatedBy))]
    public static partial void UpdateEntity(UpdateSettingGroupDto dto, [MappingTarget] SettingGroup entity);
}
