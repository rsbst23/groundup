using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services;

/// <summary>
/// Resolves a tenant from the HTTP Host header by extracting the subdomain
/// and matching it against the configured default domain. Uses
/// <see cref="ITenantRepository.GetBySlugBypassFilterAsync"/> because this service
/// runs before any tenant context is established.
/// </summary>
public sealed class HostTenantResolver : IHostTenantResolver
{
    private const string DefaultDomainSettingKey = "auth.application.default-domain";

    private readonly ISettingsService _settingsService;
    private readonly ITenantRepository _tenantRepository;

    /// <summary>
    /// Initializes a new instance of <see cref="HostTenantResolver"/>.
    /// </summary>
    /// <param name="settingsService">The settings resolution service.</param>
    /// <param name="tenantRepository">The tenant repository for slug lookups.</param>
    public HostTenantResolver(
        ISettingsService settingsService,
        ITenantRepository tenantRepository)
    {
        _settingsService = settingsService;
        _tenantRepository = tenantRepository;
    }

    /// <inheritdoc />
    public async Task<TenantDto?> ResolveAsync(HostString host, CancellationToken cancellationToken = default)
    {
        // 1. Read the default domain from settings.
        var domainResult = await _settingsService.GetAsync<string>(DefaultDomainSettingKey, cancellationToken);

        // If the setting is empty/null → return null for all requests (resolution disabled).
        if (!domainResult.Success || string.IsNullOrWhiteSpace(domainResult.Data))
        {
            return null;
        }

        var defaultDomain = domainResult.Data.Trim().ToLowerInvariant();

        // 2. Extract hostname, stripping port.
        var hostValue = host.Host;
        if (string.IsNullOrWhiteSpace(hostValue))
        {
            return null;
        }

        var hostname = hostValue.ToLowerInvariant();

        // 3. Return null for IP addresses (IPv4 or IPv6 bracket notation).
        if (IsIpAddress(hostname))
        {
            return null;
        }

        // 4. Check that the hostname ends with the default domain.
        //    The hostname must be longer than the domain (has a subdomain prefix).
        if (!hostname.EndsWith($".{defaultDomain}", StringComparison.Ordinal))
        {
            return null;
        }

        // 5. Extract the prefix (everything before ".{defaultDomain}").
        var prefixLength = hostname.Length - defaultDomain.Length - 1; // -1 for the dot
        var prefix = hostname[..prefixLength];

        // 6. Return null for multi-level subdomains (prefix contains a dot).
        if (prefix.Contains('.'))
        {
            return null;
        }

        // 7. Return null for empty prefix (bare domain — shouldn't happen given EndsWith check, but defensive).
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        // 8. The prefix is the candidate slug. Look it up (already lowercased).
        var tenantResult = await _tenantRepository.GetBySlugBypassFilterAsync(prefix, cancellationToken);

        if (!tenantResult.Success || tenantResult.Data is null)
        {
            return null;
        }

        // 9. Return the tenant only if it is active.
        return tenantResult.Data.IsActive ? tenantResult.Data : null;
    }

    /// <summary>
    /// Determines whether the given hostname is an IP address (IPv4 or IPv6).
    /// </summary>
    private static bool IsIpAddress(string hostname)
    {
        // IPv6 bracket notation (e.g., "[::1]")
        if (hostname.StartsWith('['))
        {
            return true;
        }

        // IPv4: all characters are digits or dots, and contains at least one dot.
        if (hostname.Contains('.') && hostname.All(c => char.IsDigit(c) || c == '.'))
        {
            return true;
        }

        // Localhost without dots could be "localhost" which is not an IP.
        // Pure numeric without dots (rare, but possible as a numeric IP form).
        if (hostname.All(char.IsDigit) && hostname.Length > 0)
        {
            return true;
        }

        return false;
    }
}
