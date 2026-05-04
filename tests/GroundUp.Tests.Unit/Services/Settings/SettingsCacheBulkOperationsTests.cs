using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests cache behavior for GetAllForScopeAsync and GetGroupAsync bulk operations.
/// </summary>
public sealed class SettingsCacheBulkOperationsTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;
    private readonly TestSettingsDbContext _context;

    public SettingsCacheBulkOperationsTests()
    {
        _fixture = new SettingsTestFixture();
        _context = _fixture.CreateContext();
    }

    [Fact]
    public async Task GetAllForScopeAsync_CacheMiss_ResolvesFromDbAndStoresInCache()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(_context,
            key: "BulkCacheSetting1",
            dataType: SettingDataType.String,
            defaultValue: "value1",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        _fixture.CacheKeyTracker.GetAllKeys().Should().Contain(k => k.StartsWith("settings:all:"));
    }

    [Fact]
    public async Task GetAllForScopeAsync_CacheHit_ReturnsCachedValue()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(_context,
            key: "BulkCacheHitSetting",
            dataType: SettingDataType.String,
            defaultValue: "original",
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // First call populates cache
        await service.GetAllForScopeAsync(scopeChain);

        // Modify the DB value directly (simulating external change)
        definition.DefaultValue = "modified";
        _context.Set<SettingDefinition>().Update(definition);
        await _context.SaveChangesAsync();

        // Act - second call should return cached value
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert - should still return original cached value
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "BulkCacheHitSetting");
        resolved.EffectiveValue.Should().Be("original");
    }

    [Fact]
    public async Task GetGroupAsync_CacheMiss_ResolvesFromDbAndStoresInCache()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(_context, "CacheGroup", "Cache Group");
        await SettingsTestFixture.SeedDefinitionAsync(_context,
            key: "GroupCacheSetting1",
            dataType: SettingDataType.String,
            defaultValue: "groupval1",
            groupId: groupId,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // Act
        var result = await service.GetGroupAsync("CacheGroup", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        _fixture.CacheKeyTracker.GetAllKeys().Should().Contain(k => k.StartsWith("settings:group:"));
    }

    [Fact]
    public async Task GetGroupAsync_CacheHit_ReturnsCachedValue()
    {
        // Arrange
        var levelId = await SettingsTestFixture.SeedLevelAsync(_context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(_context, "CacheHitGroup", "Cache Hit Group");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(_context,
            key: "GroupCacheHitSetting",
            dataType: SettingDataType.String,
            defaultValue: "original_group",
            groupId: groupId,
            allowedLevelIds: levelId);

        var service = _fixture.CreateService(_context);
        var scopeChain = new List<SettingScopeEntry> { new(levelId, null) };

        // First call populates cache
        await service.GetGroupAsync("CacheHitGroup", scopeChain);

        // Modify the DB value directly (simulating external change)
        definition.DefaultValue = "modified_group";
        _context.Set<SettingDefinition>().Update(definition);
        await _context.SaveChangesAsync();

        // Act - second call should return cached value
        var result = await service.GetGroupAsync("CacheHitGroup", scopeChain);

        // Assert - should still return original cached value
        result.Success.Should().BeTrue();
        var resolved = result.Data!.First(r => r.Definition.Key == "GroupCacheHitSetting");
        resolved.EffectiveValue.Should().Be("original_group");
    }

    public void Dispose()
    {
        _context.Dispose();
        _fixture.Dispose();
    }
}
