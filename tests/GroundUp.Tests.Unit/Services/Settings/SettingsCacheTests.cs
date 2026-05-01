using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Events;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Unit tests for the in-memory cache behavior in <see cref="SettingsService"/>.
/// </summary>
public sealed class SettingsCacheTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;
    private readonly TestSettingsDbContext _context;

    public SettingsCacheTests()
    {
        _fixture = new SettingsTestFixture();
        _context = _fixture.CreateContext();
    }

    [Fact]
    public async Task GetAsync_CacheMiss_ResolvesFromDbAndStoresInCache()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            _context, "CacheTest", SettingDataType.String, "default-value", allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = Array.Empty<SettingScopeEntry>();

        // Act
        var result = await service.GetAsync<string>("CacheTest", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("default-value");

        // Verify the cache key was tracked
        _fixture.CacheKeyTracker.GetAllKeys().Should().Contain(k => k.StartsWith("settings:get:CacheTest:"));
    }

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsCachedValueWithoutDbQuery()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            _context, "CacheHitTest", SettingDataType.String, "original-value", allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = Array.Empty<SettingScopeEntry>();

        // First call populates cache
        await service.GetAsync<string>("CacheHitTest", scopeChain);

        // Modify the DB value directly (simulating external change)
        definition.DefaultValue = "modified-value";
        _context.Set<SettingDefinition>().Update(definition);
        await _context.SaveChangesAsync();

        // Act - second call should return cached value
        var result = await service.GetAsync<string>("CacheHitTest", scopeChain);

        // Assert - should still return original cached value
        result.Success.Should().BeTrue();
        result.Data.Should().Be("original-value");
    }

    [Fact]
    public async Task SetAsync_DoesNotCache()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(
            _context, "SetTest", SettingDataType.String, "default", allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);

        // Act
        await service.SetAsync("SetTest", "new-value", levelId, null);

        // Assert - no cache keys should be tracked for set operations
        _fixture.CacheKeyTracker.GetAllKeys().Should().NotContain(k => k.Contains("SetTest"));
    }

    [Fact]
    public async Task DeleteValueAsync_DoesNotCache()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(
            _context, "DeleteTest", SettingDataType.String, "default", allowedLevelIds: levelId);
        var valueId = await SettingsTestFixture.SeedValueAsync(_context, definition.Id, levelId, null, "some-value");

        var service = _fixture.CreateService(_context);

        // Act
        await service.DeleteValueAsync(valueId);

        // Assert - no cache keys should be tracked for delete operations
        _fixture.CacheKeyTracker.GetAllKeys().Should().NotContain(k => k.Contains("DeleteTest"));
    }

    [Fact]
    public async Task GetAsync_CacheExceptionOnRead_FallsThroughToDb()
    {
        // Arrange - use a throwing cache
        var throwingCache = Substitute.For<IMemoryCache>();
        throwingCache.TryGetValue(Arg.Any<object>(), out Arg.Any<object?>()!)
            .Returns(x => throw new InvalidOperationException("Cache failure"));

        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(
            _context, "CacheFailTest", SettingDataType.String, "fallback-value", allowedLevelIds: levelId);

        var service = new SettingsService(
            _context,
            _fixture.EventBus,
            _fixture.ScopeChainProvider,
            throwingCache,
            _fixture.CacheOptions,
            _fixture.CacheKeyTracker);

        var scopeChain = Array.Empty<SettingScopeEntry>();

        // Act
        var result = await service.GetAsync<string>("CacheFailTest", scopeChain);

        // Assert - should still resolve from DB
        result.Success.Should().BeTrue();
        result.Data.Should().Be("fallback-value");
    }

    public void Dispose()
    {
        _context.Dispose();
        _fixture.Dispose();
    }
}
