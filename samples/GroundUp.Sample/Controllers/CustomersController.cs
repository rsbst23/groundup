using GroundUp.Api.Controllers;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Simple CRUD controller — base classes handle everything.
/// Demonstrates the "just works" pattern for simple entities.
/// </summary>
public class CustomersController : BaseController<CustomerDto>
{
    public CustomersController(BaseService<CustomerDto> service) : base(service) { }
}
