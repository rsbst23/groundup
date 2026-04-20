using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Services;

namespace GroundUp.Tests.Unit.Api.TestHelpers;

public class TestBaseService : BaseService<ControllerTestDto>
{
    public TestBaseService(IBaseRepository<ControllerTestDto> repository, IEventBus eventBus)
        : base(repository, eventBus) { }
}
