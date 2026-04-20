using GroundUp.Core.Entities;

namespace GroundUp.Sample.Entities;

public class Customer : BaseEntity, IAuditable
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    // IAuditable
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
