using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceWriteValidatorTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task SetAsync_NoValidatorsRegistered_SavesValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "S1", allowedLevelIds: levelId);
        var service = _fixture.CreateService(context);

        // Act
        var result = await service.SetAsync("S1", "v", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_ValidatorReturnsError_ReturnsBadRequestAndDoesNotSave()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "S1", allowedLevelIds: levelId);

        var validator = Substitute.For<ISettingWriteValidator>();
        validator.ValidateAsync(Arg.Any<SettingWriteContext>(), Arg.Any<CancellationToken>())
            .Returns("not allowed by rule");
        var service = _fixture.CreateServiceWithValidators(context, validator);

        // Act
        var result = await service.SetAsync("S1", "v", levelId, null);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("not allowed by rule");
        (await context.Set<SettingValue>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetAsync_ValidatorReturnsNull_SavesValue()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "S1", allowedLevelIds: levelId);

        var validator = Substitute.For<ISettingWriteValidator>();
        validator.ValidateAsync(Arg.Any<SettingWriteContext>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var service = _fixture.CreateServiceWithValidators(context, validator);

        // Act
        var result = await service.SetAsync("S1", "v", levelId, null);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_ValidatorRegistered_ReceivesKeyValueLevelNameAndScope()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "S1", allowedLevelIds: levelId);
        var scopeId = Guid.NewGuid();

        SettingWriteContext? captured = null;
        var validator = Substitute.For<ISettingWriteValidator>();
        validator.ValidateAsync(Arg.Do<SettingWriteContext>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var service = _fixture.CreateServiceWithValidators(context, validator);

        // Act
        await service.SetAsync("S1", "42", levelId, scopeId);

        // Assert
        captured.Should().NotBeNull();
        captured!.Key.Should().Be("S1");
        captured.Value.Should().Be("42");
        captured.LevelId.Should().Be(levelId);
        captured.LevelName.Should().Be("Tenant");
        captured.ScopeId.Should().Be(scopeId);
    }

    [Fact]
    public async Task SetAsync_BuiltInCheckFails_ValidatorNotCalled()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "S1", dataType: SettingDataType.Int, minValue: "10", allowedLevelIds: levelId);

        var validator = Substitute.For<ISettingWriteValidator>();
        var service = _fixture.CreateServiceWithValidators(context, validator);

        // Act
        var result = await service.SetAsync("S1", "5", levelId, null);

        // Assert
        result.StatusCode.Should().Be(400);
        await validator.DidNotReceive().ValidateAsync(Arg.Any<SettingWriteContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureDefinitionAsync_MinAndMaxProvided_PersistsBothAndSetAsyncEnforcesThem()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var service = _fixture.CreateService(context);

        // Act
        var ensured = await service.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.test.bounded",
            DataType: SettingDataType.Int,
            DefaultValue: "60",
            DisplayName: "Bounded",
            Description: null,
            Category: null,
            GroupKey: "g",
            GroupDisplayName: "G",
            AllowedLevelNames: ["system"],
            MinValue: "2",
            MaxValue: "120"));

        var tooLow = await service.SetAsync("auth.test.bounded", "1", levelId, null);
        var tooHigh = await service.SetAsync("auth.test.bounded", "121", levelId, null);
        var ok = await service.SetAsync("auth.test.bounded", "30", levelId, null);

        // Assert
        ensured.Data!.MinValue.Should().Be("2");
        ensured.Data.MaxValue.Should().Be("120");
        tooLow.StatusCode.Should().Be(400);
        tooHigh.StatusCode.Should().Be(400);
        ok.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_FiveLevelChain_MostSpecificLevelWinsAndParentsAreDefaults()
    {
        // Arrange
        using var context = _fixture.CreateContext();
        var userL = await SettingsTestFixture.SeedLevelAsync(context, "User");
        var groupL = await SettingsTestFixture.SeedLevelAsync(context, "Group");
        var caseL = await SettingsTestFixture.SeedLevelAsync(context, "Case");
        var tenantL = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var systemL = await SettingsTestFixture.SeedLevelAsync(context, "System");

        var def = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Depth",
            dataType: SettingDataType.Int,
            defaultValue: "0",
            allowedLevelIds: new[] { userL, groupL, caseL, tenantL, systemL });

        var user = Guid.NewGuid();
        var group = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var tenant = Guid.NewGuid();

        await SettingsTestFixture.SeedValueAsync(context, def.Id, systemL, null, "1");
        await SettingsTestFixture.SeedValueAsync(context, def.Id, tenantL, tenant, "2");
        await SettingsTestFixture.SeedValueAsync(context, def.Id, caseL, caseId, "3");
        await SettingsTestFixture.SeedValueAsync(context, def.Id, groupL, group, "4");

        var service = _fixture.CreateService(context);

        List<SettingScopeEntry> Chain() =>
        [
            new(userL, user), new(groupL, group), new(caseL, caseId), new(tenantL, tenant), new(systemL, null)
        ];

        // Act + Assert: no user value, so Group (4) wins over Case, Tenant and System
        (await service.GetAsync<int>("Depth", Chain())).Data.Should().Be(4);

        // A user value now wins over everything above it. A new key avoids the cache.
        var def2 = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Depth2",
            dataType: SettingDataType.Int,
            defaultValue: "0",
            allowedLevelIds: new[] { userL, groupL, caseL, tenantL, systemL });
        await SettingsTestFixture.SeedValueAsync(context, def2.Id, systemL, null, "1");
        await SettingsTestFixture.SeedValueAsync(context, def2.Id, tenantL, tenant, "2");
        await SettingsTestFixture.SeedValueAsync(context, def2.Id, userL, user, "5");
        (await service.GetAsync<int>("Depth2", Chain())).Data.Should().Be(5);

        // Only System set: every lower level inherits it
        var def3 = await SettingsTestFixture.SeedDefinitionAsync(context,
            key: "Depth3",
            dataType: SettingDataType.Int,
            defaultValue: "0",
            allowedLevelIds: new[] { userL, groupL, caseL, tenantL, systemL });
        await SettingsTestFixture.SeedValueAsync(context, def3.Id, systemL, null, "1");
        (await service.GetAsync<int>("Depth3", Chain())).Data.Should().Be(1);
    }

    public void Dispose() => _fixture.Dispose();
}
