using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;

namespace GroundUp.Services.Settings;

/// <summary>
/// Handles <see cref="SettingChangedEvent"/> by invalidating relevant cache entries.
/// When a setting value changes, this handler removes:
/// <list type="bullet">
///   <item>All cache entries for the specific setting key (all scope chain variations).</item>
///   <item>All bulk cache entries (<c>settings:all:*</c> and <c>settings:group:*</c>) since
///   any setting change could affect them.</item>
/// </list>
/// Exceptions during cache removal are caught and swallowed — stale data until TTL
/// expiry is acceptable as a fallback.
/// </summary>
public sealed class SettingsCacheInvalidationHandler : IEventHandler<SettingChangedEvent>
{
    private readonly IMemoryCache _cache;
    private readonly SettingsCacheKeyTracker _cacheKeyTracker;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsCacheInvalidationHandler"/>.
    /// </summary>
    /// <param name="cache">The in-memory cache to invalidate entries from.</param>
    /// <param name="cacheKeyTracker">Shared tracker for active cache keys.</param>
    public SettingsCacheInvalidationHandler(IMemoryCache cache, SettingsCacheKeyTracker cacheKeyTracker)
    {
        _cache = cache;
        _cacheKeyTracker = cacheKeyTracker;
    }

    /// <inheritdoc />
    public Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            var keysToRemove = _cacheKeyTracker.GetAllKeys()
                .Where(k =>
                    k.StartsWith($"settings:get:{@event.SettingKey}:", StringComparison.Ordinal) ||
                    k.StartsWith("settings:all:", StringComparison.Ordinal) ||
                    k.StartsWith("settings:group:", StringComparison.Ordinal))
                .ToList();

            foreach (var cacheKey in keysToRemove)
            {
                _cache.Remove(cacheKey);
                _cacheKeyTracker.Remove(cacheKey);
            }
        }
        catch
        {
            // Exceptions during cache removal are swallowed.
            // Stale data until TTL expiry is acceptable.
        }

        return Task.CompletedTask;
    }
}
