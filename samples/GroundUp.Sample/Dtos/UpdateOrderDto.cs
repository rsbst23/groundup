namespace GroundUp.Sample.Dtos;

/// <summary>
/// Input DTO for updating an order — only fields that can be changed.
/// </summary>
public class UpdateOrderDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
