namespace GroundUp.Sample.Dtos;

/// <summary>
/// Input DTO for updating an order — only fields that can be changed.
/// </summary>
public class UpdateOrderDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public bool IsUrgent { get; set; }
    public string Priority { get; set; } = "Normal";
    public double ShippingWeight { get; set; }
    public long TrackingNumber { get; set; }
    public DateOnly? ShipDate { get; set; }
    public int? DiscountPercent { get; set; }
}
