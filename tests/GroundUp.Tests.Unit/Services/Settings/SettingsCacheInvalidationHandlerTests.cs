using FluentAssertions;
using GroundUp.Events;
using GroundUp.Services.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsCacheInvalidationHandler"/>.
/// </summary>
public sealed class SettingsCacheInvalidationHandlerTests : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly SettingsCacheKeyTracker _tracker;
    private readonly SettingsCacheInvalidationHandler _handler;

    public SettingsCacheInvalidationHandlerTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _tracker = new SettingsCacheKeyTracker();
        _handler = new SettingsCacheInvalidationHandler(_cache, _tracker);
    }

    [Fact]
    public async Task HandleAsync_EventReceived_ClearsEntriesForChangedKey()
    {
        // Arrange - populate cache with entries for the setting key
        var cacheKey1 = "settings:get:MaxUploadSizeMB:12345";
        var cacheKey2 = "settings:get:MaxUploadSizeMB:67890";
        var cacheKeyOther = "settings:get:AppTheme:12345";

        _cache.Set(cacheKey1, "value1");
        _cache.Set(cacheKey2, "value2");
        _cache.Set(cacheKeyOther, "other-value");
        _tracker.Track(cacheKey1);
        _tracker.Track(cacheKey2);
        _tracker.Track(cacheKeyOther);

        var @event = new SettingChangedEvent
        {
            SettingKey = "MaxUploadSizeMB",
            LevelId = Guid.NewGuid(),
            NewValue = "100"
        };

        // Act
        await _handler.HandleAsync(@event);

        // Assert - MaxUploadSizeMB entries should be removed
        _cache.TryGetValue(cacheKey1, out _).Should().BeFalse();
        _cache.TryGetValue(cacheKey2, out _).Should().BeFalse();
        // Other setting should remain
        _cache.TryGetValue(cacheKeyOther, out _).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_EventReceived_ClearsBulkCacheEntries()
    {
        // Arrange - populate cache with bulk entries
        var allKey = "settings:all:12345";
        var groupKey = "settings:group:DatabaseConnection:12345";
        var specificKey = "settings:get:AppTheme:99999";

        _cache.Set(allKey, "all-data");
        _cache.Set(groupKey, "group-data");
        _cache.Set(specificKey, "specific-data");
        _tracker.Track(allKey);
        _tracker.Track(groupKey);
        _tracker.Track(specificKey);

        var @event = new SettingChangedEvent
        {
            SettingKey = "SomeOtherSetting",
            LevelId = Guid.NewGuid(),
            NewValue = "new"
        };

        // Act
        await _handler.HandleAsync(@event);

        // Assert - bulk entries should be removed
        _cache.TryGetValue(allKey, out _).Should().BeFalse();
        _cache.TryGetValue(groupKey, out _).Should().BeFalse();
        // Specific entry for a different key should remain
        _cache.TryGetValue(specificKey, out _).Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ExceptionDuringRemoval_DoesNotPropagate()
    {
        // Arrange - use a disposed cache that will throw
        var disposedCache = new MemoryCache(new MemoryCacheOptions());
        var tracker = new SettingsCacheKeyTracker();
        tracker.Track("settings:get:Test:123");
        disposedCache.Dispose(); // This will cause Remove to throw

        var handler = new SettingsCacheInvalidationHandler(disposedCache, tracker);

        var @event = new SettingChangedEvent
        {
            SettingKey = "Test",
            LevelId = Guid.NewGuid(),
            NewValue = "value"
        };

        // Act & Assert - should not throw
        var act = () => handler.HandleAsync(@event);
        await act.Should().NotThrowAsync();
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
