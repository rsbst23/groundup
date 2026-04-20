using FluentValidation;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

public class TodoItemService : BaseService<TodoItemDto>
{
    public TodoItemService(
        IBaseRepository<TodoItemDto> repository,
        IEventBus eventBus,
        IValidator<TodoItemDto>? validator = null)
        : base(repository, eventBus, validator)
    {
    }
}
