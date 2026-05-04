using FluentAssertions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Sample.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Tests that the DefaultSettingsSeeder is idempotent — running it multiple times
/// produces the same result as running it once.
/// </summary>
public sealed class DefaultSettingsSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DefaultSettingsSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    private SampleDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new SampleDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SeedAsync_FirstRun_CreatesExpectedData()
    {
        // Arrange
        using var context = CreateContext();
        var seeder = new DefaultSettingsSeeder(context);

        // Act
        await seeder.SeedAsync();

        // Assert — 2 levels: System, Tenant
        var levels = await context.Set<SettingLevel>().ToListAsync();
        levels.Should().HaveCount(2);
        levels.Select(l => l.Name).Should().Contain("System");
        levels.Select(l => l.Name).Should().Contain("Tenant");

        // Assert — 1 group: DatabaseConnection
        var groups = await context.Set<SettingGroup>().ToListAsync();
        groups.Should().HaveCount(1);
        groups[0].Key.Should().Be("DatabaseConnection");

        // Assert — 5 definitions
        var definitions = await context.Set<SettingDefinition>().ToListAsync();
        definitions.Should().HaveCount(5);
        definitions.Select(d => d.Key).Should().Contain("MaxUploadSizeMB");
        definitions.Select(d => d.Key).Should().Contain("AppTheme");
        definitions.Select(d => d.Key).Should().Contain("DatabaseConnection.Host");
        definitions.Select(d => d.Key).Should().Contain("DatabaseConnection.Port");
        definitions.Select(d => d.Key).Should().Contain("DatabaseConnection.Database");
    }

    [Fact]
    public async Task SeedAsync_SecondRun_DoesNotDuplicateData()
    {
        // Arrange
        using var context = CreateContext();
        var seeder = new DefaultSettingsSeeder(context);

        // Act — run twice
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        // Assert — counts should be the same as after first run
        var levels = await context.Set<SettingLevel>().ToListAsync();
        levels.Should().HaveCount(2);

        var groups = await context.Set<SettingGroup>().ToListAsync();
        groups.Should().HaveCount(1);

        var definitions = await context.Set<SettingDefinition>().ToListAsync();
        definitions.Should().HaveCount(5);
    }

    [Fact]
    public async Task SeedAsync_ThirdRun_StillIdempotent()
    {
        // Arrange
        using var context = CreateContext();
        var seeder = new DefaultSettingsSeeder(context);

        // Act — run three times
        await seeder.SeedAsync();
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        // Assert — counts should be unchanged
        var levels = await context.Set<SettingLevel>().ToListAsync();
        levels.Should().HaveCount(2);

        var groups = await context.Set<SettingGroup>().ToListAsync();
        groups.Should().HaveCount(1);

        var definitions = await context.Set<SettingDefinition>().ToListAsync();
        definitions.Should().HaveCount(5);
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
