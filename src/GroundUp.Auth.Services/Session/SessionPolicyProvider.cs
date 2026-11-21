using GroundUp.Auth.Core;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Session;

/// <summary>
/// Resolves session timeouts from the <c>auth.session.*</c> settings. The scope chain is built
/// here from level names (Tenant, then System) instead of from the request's tenant context,
/// because the token refresh middleware runs before tenant resolution.
/// <para>
/// Rules: a tenant value is never longer than the System value; the absolute lifetime is never
/// shorter than the token lifetime; if the settings cannot be read, the <see cref="AuthOptions"/>
/// values are used.
/// </para>
/// </summary>
public sealed class SessionPolicyProvider : ISessionPolicyProvider
{
    private const string LevelCacheKey = "auth:session:setting-level-ids";
    private static readonly TimeSpan LevelCacheDuration = TimeSpan.FromMinutes(15);

    private readonly ISettingsService _settingsService;
    private readonly ISettingsAdminService _settingsAdminService;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<SessionPolicyProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SessionPolicyProvider"/>.
    /// </summary>
    /// <param name="settingsService">Settings resolution (uses the settings cache).</param>
    /// <param name="settingsAdminService">Used to look up the System and Tenant level IDs.</param>
    /// <param name="cache">Caches the level IDs.</param>
    /// <param name="options">Fallback values when settings cannot be read.</param>
    /// <param name="logger">Logger.</param>
    public SessionPolicyProvider(
        ISettingsService settingsService,
        ISettingsAdminService settingsAdminService,
        IMemoryCache cache,
        IOptions<AuthOptions> options,
        ILogger<SessionPolicyProvider> logger)
    {
        _settingsService = settingsService;
        _settingsAdminService = settingsAdminService;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SessionPolicy> GetSystemPolicyAsync(CancellationToken cancellationToken = default)
        => ResolveAsync(null, cancellationToken);

    /// <inheritdoc />
    public Task<SessionPolicy> GetPolicyForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => ResolveAsync(tenantId, cancellationToken);

    private async Task<SessionPolicy> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var fallbackToken = _options.Value.TokenExpirationMinutes;
        var fallbackAbsolute = _options.Value.AbsoluteSessionLifetimeMinutes;

        int token;
        int absolute;

        try
        {
            var levels = await GetLevelIdsAsync(cancellationToken);
            if (levels.SystemLevelId is null)
            {
                _logger.LogWarning("Session settings unavailable: the System setting level was not found. Using AuthOptions values.");
                return ToPolicy(fallbackToken, fallbackAbsolute);
            }

            var systemChain = new List<SettingScopeEntry> { new(levels.SystemLevelId.Value, null) };
            var systemToken = await ReadAsync(SessionSettingKeys.TokenLifetimeMinutes, systemChain, cancellationToken);
            var systemAbsolute = await ReadAsync(SessionSettingKeys.AbsoluteLifetimeMinutes, systemChain, cancellationToken);

            token = systemToken ?? fallbackToken;
            absolute = systemAbsolute ?? fallbackAbsolute;

            if (tenantId is not null && levels.TenantLevelId is not null)
            {
                var tenantChain = new List<SettingScopeEntry>
                {
                    new(levels.TenantLevelId.Value, tenantId.Value),
                    new(levels.SystemLevelId.Value, null)
                };

                var tenantToken = await ReadAsync(SessionSettingKeys.TokenLifetimeMinutes, tenantChain, cancellationToken);
                var tenantAbsolute = await ReadAsync(SessionSettingKeys.AbsoluteLifetimeMinutes, tenantChain, cancellationToken);

                // A tenant may only shorten the System value.
                if (tenantToken is not null)
                {
                    token = Math.Min(tenantToken.Value, token);
                }

                if (tenantAbsolute is not null)
                {
                    absolute = Math.Min(tenantAbsolute.Value, absolute);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Session settings could not be read. Using AuthOptions values.");
            return ToPolicy(fallbackToken, fallbackAbsolute);
        }

        return ToPolicy(token, absolute);
    }

    private SessionPolicy ToPolicy(int tokenMinutes, int absoluteMinutes)
    {
        if (absoluteMinutes < tokenMinutes)
        {
            _logger.LogWarning(
                "Absolute session lifetime ({AbsoluteMinutes} min) is shorter than the token lifetime ({TokenMinutes} min). Using the absolute lifetime for both.",
                absoluteMinutes, tokenMinutes);
            tokenMinutes = absoluteMinutes;
        }

        return new SessionPolicy(TimeSpan.FromMinutes(tokenMinutes), TimeSpan.FromMinutes(absoluteMinutes));
    }

    private async Task<int?> ReadAsync(
        string key,
        IReadOnlyList<SettingScopeEntry> chain,
        CancellationToken cancellationToken)
    {
        var result = await _settingsService.GetAsync<int>(key, chain, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("Session setting {SettingKey} could not be resolved: {Reason}", key, result.Message);
            return null;
        }

        return result.Data;
    }

    private async Task<LevelIds> GetLevelIdsAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(LevelCacheKey, out LevelIds? cached) && cached is not null)
        {
            return cached;
        }

        var result = await _settingsAdminService.GetAllLevelsAsync(cancellationToken);
        if (!result.Success || result.Data is null)
        {
            return new LevelIds(null, null);
        }

        var system = result.Data.FirstOrDefault(l => string.Equals(l.Name, "System", StringComparison.OrdinalIgnoreCase));
        var tenant = result.Data.FirstOrDefault(l => string.Equals(l.Name, "Tenant", StringComparison.OrdinalIgnoreCase));
        var ids = new LevelIds(system?.Id, tenant?.Id);

        // Do not cache an incomplete answer: the levels may be seeded a moment later.
        if (ids.SystemLevelId is not null && ids.TenantLevelId is not null)
        {
            _cache.Set(LevelCacheKey, ids, LevelCacheDuration);
        }

        return ids;
    }

    private sealed record LevelIds(Guid? SystemLevelId, Guid? TenantLevelId);
}
