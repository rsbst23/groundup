using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Sample.Mappers;

[Mapper]
public static partial class TodoItemMapper
{
    public static partial TodoItemDto ToDto(TodoItem entity);
    public static partial TodoItem ToEntity(TodoItemDto dto);
}
