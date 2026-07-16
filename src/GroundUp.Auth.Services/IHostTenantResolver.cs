using Microsoft.AspNetCore.Http;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Resolves a tenant from the HTTP Host header by matching the subdomain
/// against the configured default domain.
/// </summary>
public interface IHostTenantResolver
{
    /// <summary>
    /// Resolves a tenant from the HTTP Host header by matching the subdomain
    /// against the configured default domain.
    /// </summary>
    /// <param name="host">The Host header value from the incoming request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved <see cref="TenantDto"/> if a matching active tenant is found; otherwise null.</returns>
    Task<TenantDto?> ResolveAsync(HostString host, CancellationToken cancellationToken = default);
}
