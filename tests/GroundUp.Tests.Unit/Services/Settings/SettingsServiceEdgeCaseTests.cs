using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Edge case tests for SettingsService covering empty databases, multi-tenant isolation,
/// three-level scope chains, and null scopeId at system level.
/// </summary>
public sealed class SettingsServiceEdgeCaseTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task GetAllForScopeAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(Guid.NewGuid(), null) };

        // Act
        var result = await service.GetAllForScopeAsync(scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupAsync_EmptyGroup_ReturnsEmptyList()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "EmptyGroup", "Empty Group");

        // Group exists but has no definitions assigned to it
        var service = _fixture.CreateService(context);
        var scopeChain = new List<SettingScopeEntry> { new(Guid.NewGuid(), null) };

        // Act
        var result = await service.GetGroupAsync("EmptyGroup", scopeChain);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SetAsync_MultipleTenants_SameKey_IndependentValues()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SharedSetting",
            dataType: SettingDataType.String,
            allowedLevelIds: tenantLevelId);

        var tenant1ScopeId = Guid.NewGuid();
        var tenant2ScopeId = Guid.NewGuid();

        var service = _fixture.CreateService(context);

        // Act - set different values for two tenants
        var result1 = await service.SetAsync("SharedSetting", "tenant1_value", tenantLevelId, tenant1ScopeId);
        var result2 = await service.SetAsync("SharedSetting", "tenant2_value", tenantLevelId, tenant2ScopeId);

        // Assert - each tenant has their own value
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        // Verify tenant 1 gets their value
        var scopeChain1 = new List<SettingScopeEntry> { new(tenantLevelId, tenant1ScopeId) };
        var get1 = await service.GetAsync<string>("SharedSetting", scopeChain1);
        get1.Success.Should().BeTrue();
        get1.Data.Should().Be("tenant1_value");

        // Verify tenant 2 gets their value
        var scopeChain2 = new List<SettingScopeEntry> { new(tenantLevelId, tenant2ScopeId) };
        var get2 = await service.GetAsync<string>("SharedSetting", scopeChain2);
        get2.Success.Should().BeTrue();
        get2.Data.Should().Be("tenant2_value");
    }

    [Fact]
    public async Task GetAsync_ThreeLevelScopeChain_ResolvesCorrectly()
    {
        // Arrange — 3 levels: User (most specific) → Tenant → System (least specific)
        using var context = _fixture.CreateContext();
        var userLevelId = await SettingsTestFixture.SeedLevelAsync(context, "User", 0);
        var tenantLevelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant", 1);
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System", 2);

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "ThreeLevelSetting",
            dataType: SettingDataType.String,
            defaultValue: "default_value",
            allowedLevelIds: new[] { userLevelId, tenantLevelId, systemLevelId });

        var tenantScopeId = Guid.NewGuid();
        var userScopeId = Guid.NewGuid();

        // Set values at system and tenant levels (but NOT user level)
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, systemLevelId, null, "system_value");
        await SettingsTestFixture.SeedValueAsync(context, definition.Id, tenantLevelId, tenantScopeId, "tenant_value");

        var service = _fixture.CreateService(context);

        // Scope chain: User → Tenant → System (most specific first)
        var scopeChain = new List<SettingScopeEntry>
        {
            new(userLevelId, userScopeId),
            new(tenantLevelId, tenantScopeId),
            new(systemLevelId, null)
        };

        // Act
        var result = await service.GetAsync<string>("ThreeLevelSetting", scopeChain);

        // Assert — tenant value wins because user level has no value set
        result.Success.Should().BeTrue();
        result.Data.Should().Be("tenant_value");
    }

    [Fact]
    public async Task SetAsync_NullScopeId_SystemLevel_Succeeds()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var systemLevelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "SystemSetting",
            dataType: SettingDataType.String,
            allowedLevelIds: systemLevelId);

        var service = _fixture.CreateService(context);

        // Act — null scopeId for system level
        var result = await service.SetAsync("SystemSetting", "system_value", systemLevelId, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.ScopeId.Should().BeNull();
        result.Data.Value.Should().Be("system_value");

        // Verify we can read it back
        var scopeChain = new List<SettingScopeEntry> { new(systemLevelId, null) };
        var getResult = await service.GetAsync<string>("SystemSetting", scopeChain);
        getResult.Success.Should().BeTrue();
        getResult.Data.Should().Be("system_value");
    }

    public void Dispose() => _fixture.Dispose();
}
