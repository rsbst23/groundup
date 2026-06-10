using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Auth.Keycloak;

/// <summary>
/// Per-issuer JWKS key cache with auto-refresh capability.
/// Keys are fetched from the issuer's JWKS endpoint and cached in memory.
/// Refresh is rate-limited to once per 30 seconds per issuer to prevent abuse.
/// </summary>
internal sealed class JwksCache
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, CachedKeySet> _keysByIssuer = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphoresByIssuer = new();
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromHours(24);

    /// <summary>
    /// Initializes a new instance of the <see cref="JwksCache"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating named clients.</param>
    public JwksCache(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns cached JWKS keys for the given issuer, or fetches them if not yet cached.
    /// </summary>
    /// <param name="issuerUrl">The issuer URL (e.g., {InternalBaseUrl}/realms/{realmName}).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of security keys for the issuer.</returns>
    public async Task<ICollection<SecurityKey>> GetKeysForIssuerAsync(string issuerUrl, CancellationToken ct)
    {
        if (_keysByIssuer.TryGetValue(issuerUrl, out var cached))
        {
            // If cache is still within max TTL, return it
            var age = DateTimeOffset.UtcNow - cached.FetchedAt;
            if (age < MaxCacheDuration)
            {
                return cached.Keys;
            }
        }

        return await FetchAndCacheKeysAsync(issuerUrl, ct);
    }

    /// <summary>
    /// Force-refreshes the JWKS keys for the given issuer. Rate-limited to once per 30 seconds
    /// per issuer to prevent abuse from repeated unknown-kid scenarios.
    /// </summary>
    /// <param name="issuerUrl">The issuer URL (e.g., {InternalBaseUrl}/realms/{realmName}).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of refreshed security keys for the issuer.</returns>
    public async Task<ICollection<SecurityKey>> RefreshKeysForIssuerAsync(string issuerUrl, CancellationToken ct)
    {
        if (_keysByIssuer.TryGetValue(issuerUrl, out var cached))
        {
            var elapsed = DateTimeOffset.UtcNow - cached.FetchedAt;
            if (elapsed < MinRefreshInterval)
            {
                // Rate-limited: return existing keys without re-fetching
                return cached.Keys;
            }
        }

        return await FetchAndCacheKeysAsync(issuerUrl, ct);
    }

    private async Task<ICollection<SecurityKey>> FetchAndCacheKeysAsync(string issuerUrl, CancellationToken ct)
    {
        var semaphore = _semaphoresByIssuer.GetOrAdd(issuerUrl, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring semaphore — another thread may have refreshed
            if (_keysByIssuer.TryGetValue(issuerUrl, out var cached))
            {
                var elapsed = DateTimeOffset.UtcNow - cached.FetchedAt;
                if (elapsed < MinRefreshInterval)
                {
                    return cached.Keys;
                }
            }

            var jwksUrl = $"{issuerUrl.TrimEnd('/')}/protocol/openid-connect/certs";
            var client = _httpClientFactory.CreateClient("KeycloakIdp");

            var response = await client.GetAsync(jwksUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var jwks = new JsonWebKeySet(json);
            var keys = jwks.GetSigningKeys();

            var keySet = new CachedKeySet(keys, DateTimeOffset.UtcNow);
            _keysByIssuer[issuerUrl] = keySet;

            return keys;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private sealed record CachedKeySet(ICollection<SecurityKey> Keys, DateTimeOffset FetchedAt);
}
