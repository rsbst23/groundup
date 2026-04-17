namespace GroundUp.Core.Entities;

/// <summary>
/// Declares tenant ownership for automatic tenant filtering
/// in BaseTenantRepository. Entities implementing this interface
/// will be automatically scoped to the current tenant.
/// </summary>
public interface ITenantEntity
{
    /// <summary>
    /// The tenant that owns this entity.
    /// </summary>
    Guid TenantId { get; set; }
}
