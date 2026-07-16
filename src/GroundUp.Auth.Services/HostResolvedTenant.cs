using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Scoped service holding the tenant resolved from the request Host header.
/// Populated by <c>HostTenantResolutionMiddleware</c> early in the pipeline.
/// </summary>
/// <remarks>
/// This is distinct from the JWT-derived <c>TenantContext</c>. The <see cref="Tenant"/>
/// property is null when no tenant was resolved (bare domain, no match, or resolution disabled).
/// </remarks>
public sealed class HostResolvedTenant
{
    /// <summary>
    /// The tenant resolved from the request Host header.
    /// Null when no tenant was resolved (bare domain, no match, resolution disabled).
    /// </summary>
    public TenantDto? Tenant { get; set; }
}
