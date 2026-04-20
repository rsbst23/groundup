using GroundUp.Core.Entities;

namespace GroundUp.Sample.Entities;

public class Order : BaseEntity, IAuditable
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "Pending";

    // Additional properties for testing all supported filter/sort data types
    public int ItemCount { get; set; }
    public bool IsUrgent { get; set; }
    public OrderPriority Priority { get; set; } = OrderPriority.Normal;
    public double ShippingWeight { get; set; }
    public long TrackingNumber { get; set; }
    public DateOnly? ShipDate { get; set; }
    public int? DiscountPercent { get; set; }

    // IAuditable
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
