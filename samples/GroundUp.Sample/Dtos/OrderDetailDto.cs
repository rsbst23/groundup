namespace GroundUp.Sample.Dtos;

/// <summary>
/// Rich DTO for order detail views — includes full customer info.
/// Used by GetByIdAsync.
/// </summary>
public class OrderDetailDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public CustomerDto Customer { get; set; } = null!;
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public bool IsUrgent { get; set; }
    public string Priority { get; set; } = string.Empty;
    public double ShippingWeight { get; set; }
    public long TrackingNumber { get; set; }
    public DateOnly? ShipDate { get; set; }
    public int? DiscountPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
