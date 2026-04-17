namespace GroundUp.Core.Abstractions;

/// <summary>
/// Provides the current tenant identity for automatic tenant filtering
/// in BaseTenantRepository. Implementations are registered as scoped
/// services and populated from request headers, JWT claims, or other sources.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current tenant's unique identifier.
    /// </summary>
    Guid TenantId { get; }
}
