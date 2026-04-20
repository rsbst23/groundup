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
}
