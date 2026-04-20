using GroundUp.Repositories;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using GroundUp.Sample.Mappers;

namespace GroundUp.Sample.Repositories;

public class CustomerRepository : BaseRepository<Customer, CustomerDto>
{
    public CustomerRepository(SampleDbContext context)
        : base(context, CustomerMapper.ToDto, CustomerMapper.ToEntity) { }
}
