using FluentAssertions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests that the parameterless convenience overloads on SettingsService correctly
/// delegate through IScopeChainProvider to the explicit overloads.
/// </summary>
public sealed class SettingsServiceConvenienceOverloadTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task GetAsync_ConvenienceOverload_DelegatesToExplicitOverloadWithProviderScopeChain()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var scopeId = Guid.NewGuid();

        var definition = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "PageSize",
            dataType: SettingDataType.Int,
            defaultValue: "10",
            allowedLevelIds: levelId);

        await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, scopeId, "42");

        var providerScopeChain = new List<SettingScopeEntry> { new(levelId, scopeId) };
        _fixture.ScopeChainProvider.GetScopeChainAsync(Arg.Any<CancellationToken>())
            .Returns(providerScopeChain);

        var service = _fixture.CreateService(context);

        // Act — convenience overload (no scope chain parameter)
        var convenienceResult = await service.GetAsync<int>("PageSize");

        // Assert — should match what the explicit overload returns with the same scope chain
        convenienceResult.Success.Should().BeTrue();
        convenienceResult.Data.Should().Be(42);
    }

    [Fact]
    public async Task GetAllForScopeAsync_ConvenienceOverload_DelegatesToExplicitOverload()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var scopeId = Guid.NewGuid();

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Theme",
            dataType: SettingDataType.String,
            defaultValue: "light",
            allowedLevelIds: levelId);

        var providerScopeChain = new List<SettingScopeEntry> { new(levelId, scopeId) };
        _fixture.ScopeChainProvider.GetScopeChainAsync(Arg.Any<CancellationToken>())
            .Returns(providerScopeChain);

        var service = _fixture.CreateService(context);

        // Act — convenience overload
        var convenienceResult = await service.GetAllForScopeAsync();

        // Also call explicit overload for comparison
        var explicitResult = await service.GetAllForScopeAsync(providerScopeChain);

        // Assert — both should return the same data
        convenienceResult.Success.Should().BeTrue();
        explicitResult.Success.Should().BeTrue();
        convenienceResult.Data.Should().HaveCount(explicitResult.Data!.Count);
        convenienceResult.Data![0].EffectiveValue.Should().Be(explicitResult.Data[0].EffectiveValue);
    }

    [Fact]
    public async Task GetGroupAsync_ConvenienceOverload_DelegatesToExplicitOverload()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var groupId = await SettingsTestFixture.SeedGroupAsync(context, "DbGroup", "Database Group");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "DbHost",
            dataType: SettingDataType.String,
            defaultValue: "localhost",
            groupId: groupId,
            allowedLevelIds: levelId);

        var providerScopeChain = new List<SettingScopeEntry> { new(levelId, null) };
        _fixture.ScopeChainProvider.GetScopeChainAsync(Arg.Any<CancellationToken>())
            .Returns(providerScopeChain);

        var service = _fixture.CreateService(context);

        // Act — convenience overload
        var convenienceResult = await service.GetGroupAsync("DbGroup");

        // Also call explicit overload for comparison
        var explicitResult = await service.GetGroupAsync("DbGroup", providerScopeChain);

        // Assert — both should return the same data
        convenienceResult.Success.Should().BeTrue();
        explicitResult.Success.Should().BeTrue();
        convenienceResult.Data.Should().HaveCount(explicitResult.Data!.Count);
        convenienceResult.Data![0].EffectiveValue.Should().Be(explicitResult.Data[0].EffectiveValue);
    }

    [Fact]
    public async Task GetAsync_ConvenienceOverload_EmptyProviderScopeChain_ReturnsDefault()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");

        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "MaxRetries",
            dataType: SettingDataType.Int,
            defaultValue: "3",
            allowedLevelIds: levelId);

        // Provider returns empty chain — should fall back to default value
        _fixture.ScopeChainProvider.GetScopeChainAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SettingScopeEntry>());

        var service = _fixture.CreateService(context);

        // Act
        var result = await service.GetAsync<int>("MaxRetries");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be(3);
    }

    public void Dispose() => _fixture.Dispose();
}
