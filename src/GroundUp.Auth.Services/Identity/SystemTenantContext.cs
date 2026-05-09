using GroundUp.Core.Abstractions;

namespace GroundUp.Auth.Services.Identity;

/// <summary>
/// Manually-constructed implementation of <see cref="ITenantContext"/> for non-HTTP scenarios
/// such as background jobs, SDK usage, or integration tests where no HTTP context is available.
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="SystemTenantContext"/> with an explicit tenant identifier.
    /// </summary>
    /// <param name="tenantId">The tenant's unique identifier.</param>
    public SystemTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }

    /// <inheritdoc />
    public Guid TenantId { get; }
}
