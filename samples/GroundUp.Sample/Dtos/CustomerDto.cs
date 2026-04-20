namespace GroundUp.Sample.Dtos;

/// <summary>
/// Simple DTO for Customer — one DTO works for all operations.
/// </summary>
public class CustomerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
