namespace GroundUp.Sample.Dtos;

/// <summary>
/// Input DTO for creating an order — only user-provided fields.
/// No Id, no audit fields, no computed fields.
/// </summary>
public class CreateOrderDto
{
    public Guid CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public bool IsUrgent { get; set; }
    public string Priority { get; set; } = "Normal";
    public double ShippingWeight { get; set; }
    public long TrackingNumber { get; set; }
    public DateOnly? ShipDate { get; set; }
    public int? DiscountPercent { get; set; }
}
