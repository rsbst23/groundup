using GroundUp.Repositories;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using GroundUp.Sample.Mappers;

namespace GroundUp.Sample.Repositories;

public class TodoItemRepository : BaseRepository<TodoItem, TodoItemDto>
{
    public TodoItemRepository(SampleDbContext context)
        : base(context, TodoItemMapper.ToDto, TodoItemMapper.ToEntity)
    {
    }
}
