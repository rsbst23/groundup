namespace GroundUp.Core.Models;

/// <summary>
/// Configuration options for the settings in-memory cache.
/// </summary>
public sealed class SettingsCacheOptions
{
    /// <summary>
    /// How long resolved settings are cached before expiring.
    /// Default is 15 minutes.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(15);
}
