using GroundUp.Core.Abstractions;
using GroundUp.Repositories;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using GroundUp.Sample.Mappers;

namespace GroundUp.Sample.Repositories;

public class ProjectRepository : BaseTenantRepository<Project, ProjectDto>
{
    public ProjectRepository(SampleDbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, ProjectMapper.ToDto, ProjectMapper.ToEntity)
    {
    }
}
