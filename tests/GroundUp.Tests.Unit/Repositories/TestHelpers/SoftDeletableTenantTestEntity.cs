using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Tenant-aware test entity implementing ISoftDeletable for soft delete unit tests.
/// </summary>
public class SoftDeletableTenantTestEntity : BaseEntity, ITenantEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
