using FluentValidation;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Services;

namespace GroundUp.Tests.Unit.Services.TestHelpers;

public class TestService : BaseService<ServiceTestDto>
{
    public TestService(
        IBaseRepository<ServiceTestDto> repository,
        IEventBus eventBus,
        IValidator<ServiceTestDto>? validator = null)
        : base(repository, eventBus, validator) { }
}
