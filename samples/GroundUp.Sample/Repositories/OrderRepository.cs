using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using GroundUp.Sample.Mappers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Sample.Repositories;

/// <summary>
/// Order repository — uses queryShaper to include Customer navigation property.
/// Uses OrderListDto as the base TDto (for GetAll), with custom methods for detail views.
/// </summary>
public class OrderRepository : BaseRepository<Order, OrderListDto>
{
    private readonly SampleDbContext _dbContext;

    public OrderRepository(SampleDbContext context)
        : base(context, OrderMapper.ToListDto, _ => throw new NotSupportedException("Use CreateOrder instead"))
    {
        _dbContext = context;
    }

    /// <summary>
    /// Override GetAllAsync to include Customer for the CustomerName field in OrderListDto.
    /// </summary>
    public override Task<OperationResult<PaginatedData<OrderListDto>>> GetAllAsync(
        FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        return base.GetAllAsync(filterParams, q => q.Include(o => o.Customer), cancellationToken);
    }

    /// <summary>
    /// Custom method: Get order detail with full customer info.
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> GetDetailByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.AsNoTracking()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (entity is null)
            return OperationResult<OrderDetailDto>.NotFound();

        return OperationResult<OrderDetailDto>.Ok(OrderMapper.ToDetailDto(entity));
    }

    /// <summary>
    /// Custom method: Create order from CreateOrderDto.
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> CreateOrderAsync(
        CreateOrderDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = OrderMapper.FromCreateDto(dto);
            DbSet.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);

            // Reload with Customer included for the response
            var created = await DbSet.AsNoTracking()
                .Include(o => o.Customer)
                .FirstAsync(o => o.Id == entity.Id, cancellationToken);

            return OperationResult<OrderDetailDto>.Ok(OrderMapper.ToDetailDto(created), "Created", 201);
        }
        catch (DbUpdateException)
        {
            return OperationResult<OrderDetailDto>.Fail(
                "A conflict occurred while saving the order.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// Custom method: Update order from UpdateOrderDto.
    /// </summary>
    public async Task<OperationResult<OrderDetailDto>> UpdateOrderAsync(
        Guid id, UpdateOrderDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);
            if (entity is null)
                return OperationResult<OrderDetailDto>.NotFound();

            entity.Status = dto.Status;
            entity.Total = dto.Total;
            await Context.SaveChangesAsync(cancellationToken);

            // Reload with Customer included for the response
            var updated = await DbSet.AsNoTracking()
                .Include(o => o.Customer)
                .FirstAsync(o => o.Id == entity.Id, cancellationToken);

            return OperationResult<OrderDetailDto>.Ok(OrderMapper.ToDetailDto(updated));
        }
        catch (DbUpdateException)
        {
            return OperationResult<OrderDetailDto>.Fail(
                "A conflict occurred while updating the order.", 409, GroundUp.Core.ErrorCodes.Conflict);
        }
    }
}
