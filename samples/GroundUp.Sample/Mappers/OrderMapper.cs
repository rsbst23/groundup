using GroundUp.Sample.Dtos;
using GroundUp.Sample.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Sample.Mappers;

[Mapper]
public static partial class OrderMapper
{
    public static partial OrderListDto ToListDto(Order entity);
    public static partial OrderDetailDto ToDetailDto(Order entity);

    public static Order FromCreateDto(CreateOrderDto dto) => new()
    {
        CustomerId = dto.CustomerId,
        OrderDate = dto.OrderDate,
        Total = dto.Total,
        ItemCount = dto.ItemCount,
        IsUrgent = dto.IsUrgent,
        Priority = Enum.TryParse<OrderPriority>(dto.Priority, true, out var p) ? p : OrderPriority.Normal,
        ShippingWeight = dto.ShippingWeight,
        TrackingNumber = dto.TrackingNumber,
        ShipDate = dto.ShipDate,
        DiscountPercent = dto.DiscountPercent,
        OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}"
    };
}
