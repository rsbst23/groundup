using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Repositories.Settings;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;
using GroundUp.Tests.Unit.Repositories;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class DefaultSettingLevelSeederTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    [Fact]
    public async Task SeedAsync_EmptyDatabase_CreatesSystemRootAndTenantChild()
    {
        using var context = _fixture.CreateContext();

        await new DefaultSettingLevelSeeder(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance)).SeedAsync();

        var levels = await context.Set<SettingLevel>().AsNoTracking().ToListAsync();
        var system = levels.Single(l => l.Name == "System");
        var tenant = levels.Single(l => l.Name == "Tenant");
        system.ParentId.Should().BeNull();
        tenant.ParentId.Should().Be(system.Id);
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicate()
    {
        using var context = _fixture.CreateContext();
        var sut = new DefaultSettingLevelSeeder(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance));

        await sut.SeedAsync();
        await sut.SeedAsync();

        (await context.Set<SettingLevel>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SeedAsync_ApplicationAlreadySeededLevels_KeepsTheirIds()
    {
        using var context = _fixture.CreateContext();
        var systemId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var tenantId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant", 1);

        await new DefaultSettingLevelSeeder(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance)).SeedAsync();

        var levels = await context.Set<SettingLevel>().AsNoTracking().ToListAsync();
        levels.Should().HaveCount(2);
        levels.Select(l => l.Id).Should().BeEquivalentTo(new[] { systemId, tenantId });
    }

    [Fact]
    public async Task SeedAsync_ThenEnsureDefinitionForSystemAndTenant_LinksBothLevels()
    {
        // The reason this seeder exists: module seeders look levels up by name when they define settings.
        using var context = _fixture.CreateContext();
        await new DefaultSettingLevelSeeder(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance)).SeedAsync();
        var service = _fixture.CreateService(context);

        await service.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "x.test",
            DataType: SettingDataType.Int,
            DefaultValue: "1",
            DisplayName: "X",
            Description: null,
            Category: null,
            GroupKey: "g",
            GroupDisplayName: "G",
            AllowedLevelNames: ["system", "tenant"]));

        var links = await context.Set<SettingDefinitionLevel>().CountAsync();
        links.Should().Be(2);
    }

    [Fact]
    public void Order_RunsBeforeModuleSeeders()
    {
        using var context = _fixture.CreateContext();

        new DefaultSettingLevelSeeder(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance)).Order.Should().BeLessThan(10);
    }

    public void Dispose() => _fixture.Dispose();
}
