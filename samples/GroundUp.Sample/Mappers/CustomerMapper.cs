using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Sample.Mappers;

[Mapper]
public static partial class CustomerMapper
{
    public static partial CustomerDto ToDto(Customer entity);
    public static partial Customer ToEntity(CustomerDto dto);
}
