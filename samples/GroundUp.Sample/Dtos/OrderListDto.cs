namespace GroundUp.Sample.Dtos;

/// <summary>
/// Flat DTO for order list views — includes customer name for grid display.
/// Used by GetAllAsync.
/// </summary>
public class OrderListDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}
