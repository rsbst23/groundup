using GroundUp.Core.Entities;

namespace GroundUp.Sample.Entities;

public class Project : BaseEntity, ITenantEntity, IAuditable
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // ITenantEntity
    public Guid TenantId { get; set; }

    // IAuditable
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
