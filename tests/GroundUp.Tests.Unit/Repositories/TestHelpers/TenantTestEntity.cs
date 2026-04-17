using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Tenant-aware test entity for BaseTenantRepository unit tests.
/// </summary>
public class TenantTestEntity : BaseEntity, ITenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
