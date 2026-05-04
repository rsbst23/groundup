using FluentAssertions;
using GroundUp.Services.Settings;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsCacheKeyTracker"/>.
/// </summary>
public sealed class SettingsCacheKeyTrackerTests
{
    [Fact]
    public void Track_AddsKeyToTrackedSet()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();
        var key = "settings:get:Theme:12345";

        // Act
        tracker.Track(key);

        // Assert
        tracker.GetAllKeys().Should().Contain(key);
    }

    [Fact]
    public void Track_DuplicateKey_DoesNotThrow()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();
        var key = "settings:get:Theme:12345";

        // Act
        tracker.Track(key);
        var act = () => tracker.Track(key);

        // Assert
        act.Should().NotThrow();
        tracker.GetAllKeys().Should().ContainSingle(k => k == key);
    }

    [Fact]
    public void Remove_ExistingKey_RemovesFromTrackedSet()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();
        var key = "settings:get:Theme:12345";
        tracker.Track(key);

        // Act
        tracker.Remove(key);

        // Assert
        tracker.GetAllKeys().Should().NotContain(key);
    }

    [Fact]
    public void Remove_NonExistentKey_DoesNotThrow()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();

        // Act
        var act = () => tracker.Remove("non-existent-key");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetAllKeys_EmptyTracker_ReturnsEmptyCollection()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();

        // Act
        var keys = tracker.GetAllKeys();

        // Assert
        keys.Should().BeEmpty();
    }

    [Fact]
    public void GetAllKeys_ReturnsSnapshotOfAllTrackedKeys()
    {
        // Arrange
        var tracker = new SettingsCacheKeyTracker();
        var key1 = "settings:get:Theme:111";
        var key2 = "settings:all:222";
        var key3 = "settings:group:General:333";

        tracker.Track(key1);
        tracker.Track(key2);
        tracker.Track(key3);

        // Act
        var keys = tracker.GetAllKeys();

        // Assert
        keys.Should().HaveCount(3);
        keys.Should().Contain(key1);
        keys.Should().Contain(key2);
        keys.Should().Contain(key3);
    }
}
