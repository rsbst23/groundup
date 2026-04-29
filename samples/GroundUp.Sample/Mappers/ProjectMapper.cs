using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Sample.Mappers;

[Mapper]
public static partial class ProjectMapper
{
    public static partial ProjectDto ToDto(Project entity);
    public static partial Project ToEntity(ProjectDto dto);
}
