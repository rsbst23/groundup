using FluentValidation;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

public class ProjectService : BaseService<ProjectDto>
{
    public ProjectService(
        IBaseRepository<ProjectDto> repository,
        IEventBus eventBus,
        IValidator<ProjectDto>? validator = null)
        : base(repository, eventBus, validator)
    {
    }
}
