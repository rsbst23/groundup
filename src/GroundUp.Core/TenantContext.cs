namespace GroundUp.Core;

/// <summary>
/// Scoped value holder implementing <see cref="Abstractions.ITenantContext"/>.
/// Infrastructure code (middleware, SDK bootstrapping, background job setup)
/// sets <see cref="TenantId"/>; downstream code (repositories, services) reads it
/// through the <see cref="Abstractions.ITenantContext"/> interface.
/// <para>
/// This class has no HTTP dependency — it works in any hosting context.
/// In HTTP scenarios, TenantResolutionMiddleware hydrates it from the request.
/// </para>
/// </summary>
public sealed class TenantContext : Abstractions.ITenantContext
{
    /// <summary>
    /// The current tenant's unique identifier.
    /// Defaults to <see cref="Guid.Empty"/> when no tenant has been resolved.
    /// </summary>
    public Guid TenantId { get; set; }
}
