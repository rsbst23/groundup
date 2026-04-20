using GroundUp.Api.Controllers;
using GroundUp.Services;

namespace GroundUp.Tests.Unit.Api.TestHelpers;

public class TestController : BaseController<ControllerTestDto>
{
    public TestController(BaseService<ControllerTestDto> service) : base(service) { }
}
