using GroundUp.Data.Abstractions;
using GroundUp.Events;
using GroundUp.Sample.Dtos;
using GroundUp.Services;

namespace GroundUp.Sample.Services;

public class CustomerService : BaseService<CustomerDto>
{
    public CustomerService(IBaseRepository<CustomerDto> repository, IEventBus eventBus)
        : base(repository, eventBus) { }
}
