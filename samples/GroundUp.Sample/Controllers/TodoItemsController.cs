using GroundUp.Api.Controllers;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Controllers;

public class TodoItemsController : BaseController<TodoItemDto>
{
    public TodoItemsController(BaseService<TodoItemDto> service) : base(service) { }
}
