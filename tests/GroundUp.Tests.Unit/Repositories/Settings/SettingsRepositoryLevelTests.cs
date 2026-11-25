using FluentAssertions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Data.Postgres;
using GroundUp.Repositories.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Repositories.Settings;

/// <summary>
/// Unit tests for the setting-level methods of <see cref="SettingsRepository"/> on SQLite in-memory.
/// </summary>
public sealed class SettingsRepositoryLevelTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SettingsRepositoryLevelTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetLevelIdByNameAsync_ExistingName_ReturnsId()
    {
        // Arrange
        var systemId = await SeedLevelAsync("System");
        await SeedLevelAsync("Tenant", systemId);
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).GetLevelIdByNameAsync("System");

        // Assert
        result.Should().Be(systemId);
    }

    [Fact]
    public async Task GetLevelIdByNameAsync_UnknownName_ReturnsNull()
    {
        // Arrange
        await SeedLevelAsync("System");
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).GetLevelIdByNameAsync("Tenant");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLevelNameByIdAsync_ExistingId_ReturnsName()
    {
        // Arrange
        var id = await SeedLevelAsync("Tenant");
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).GetLevelNameByIdAsync(id);

        // Assert
        result.Should().Be("Tenant");
    }

    [Fact]
    public async Task GetLevelNameByIdAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).GetLevelNameByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task EnsureLevelAsync_Missing_CreatesWithParentAndDisplayOrder()
    {
        // Arrange
        var systemId = await SeedLevelAsync("System");
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).EnsureLevelAsync("Tenant", systemId, 1);

        // Assert
        result.Name.Should().Be("Tenant");
        result.ParentId.Should().Be(systemId);
        result.DisplayOrder.Should().Be(1);

        await using var verify = CreateContext();
        var stored = await verify.Set<SettingLevel>().AsNoTracking().SingleAsync(l => l.Name == "Tenant");
        stored.Id.Should().Be(result.Id);
        stored.ParentId.Should().Be(systemId);
        stored.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public async Task EnsureLevelAsync_Existing_ReturnsItUnchanged()
    {
        // Arrange
        var existingId = await SeedLevelAsync("System", displayOrder: 7);
        await using var context = CreateContext();

        // Act
        var result = await new SettingsRepository(context).EnsureLevelAsync("System", null, 0);

        // Assert
        result.Id.Should().Be(existingId);
        result.DisplayOrder.Should().Be(7);

        await using var verify = CreateContext();
        (await verify.Set<SettingLevel>().CountAsync(l => l.Name == "System")).Should().Be(1);
    }

    private async Task<Guid> SeedLevelAsync(string name, Guid? parentId = null, int displayOrder = 0)
    {
        await using var context = CreateContext();
        var level = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = parentId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(level);
        await context.SaveChangesAsync();
        return level.Id;
    }

    private TestLevelDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestLevelDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TestLevelDbContext(options);
    }

    private sealed class TestLevelDbContext : GroundUpDbContext
    {
        public TestLevelDbContext(DbContextOptions<TestLevelDbContext> options)
            : base(options) { }
    }
}
