using System.Collections.Concurrent;

namespace GroundUp.Services.Settings;

/// <summary>
/// Tracks active cache keys for the settings cache, enabling targeted invalidation.
/// Shared between <see cref="SettingsService"/> (which adds keys on cache population)
/// and <see cref="SettingsCacheInvalidationHandler"/> (which removes keys on invalidation).
/// Registered as a singleton in the DI container.
/// </summary>
public sealed class SettingsCacheKeyTracker
{
    private readonly ConcurrentDictionary<string, byte> _activeKeys = new();

    /// <summary>
    /// Tracks a cache key as active.
    /// </summary>
    /// <param name="cacheKey">The cache key to track.</param>
    public void Track(string cacheKey)
    {
        _activeKeys.TryAdd(cacheKey, 0);
    }

    /// <summary>
    /// Removes a cache key from tracking.
    /// </summary>
    /// <param name="cacheKey">The cache key to remove.</param>
    public void Remove(string cacheKey)
    {
        _activeKeys.TryRemove(cacheKey, out _);
    }

    /// <summary>
    /// Gets all currently tracked cache keys.
    /// </summary>
    /// <returns>A snapshot of all active cache keys.</returns>
    public IReadOnlyCollection<string> GetAllKeys()
    {
        return _activeKeys.Keys.ToArray();
    }
}
