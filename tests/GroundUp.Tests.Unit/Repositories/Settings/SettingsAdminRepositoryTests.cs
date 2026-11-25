using FluentAssertions;
using GroundUp.Core;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Data.Postgres;
using GroundUp.Repositories.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Repositories.Settings;

/// <summary>
/// Unit tests for <see cref="SettingsAdminRepository"/> on SQLite in-memory. The repository has no
/// duplicate pre-checks (those are in the service), so calling it with a taken key reproduces what a
/// concurrent create hits: the unique index.
/// </summary>
public sealed class SettingsAdminRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SettingsAdminRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task CreateGroupAsync_KeyTaken_Returns409ConflictAndClearsTracker()
    {
        // Arrange
        await using (var seed = CreateContext())
        {
            await CreateRepository(seed).CreateGroupAsync(new CreateSettingGroupDto("email", "Email", null, null, 0));
        }

        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).CreateGroupAsync(new CreateSettingGroupDto("email", "Again", null, null, 1));

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Message.Should().Be("A setting group with key 'email' already exists");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDefinitionAsync_KeyTaken_Returns409Conflict()
    {
        // Arrange
        await using (var seed = CreateContext())
        {
            (await CreateRepository(seed).CreateDefinitionAsync(DefinitionDto("app.theme"))).Success.Should().BeTrue();
        }

        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).CreateDefinitionAsync(DefinitionDto("app.theme"));

        // Assert
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Message.Should().Be("A setting definition with key 'app.theme' already exists");
    }

    [Fact]
    public async Task UpdateDefinitionAsync_KeyTakenByAnother_Returns409AndLeavesRowsUnchanged()
    {
        // Arrange
        Guid secondId;
        await using (var seed = CreateContext())
        {
            var repository = CreateRepository(seed);
            await repository.CreateDefinitionAsync(DefinitionDto("first"));
            secondId = (await repository.CreateDefinitionAsync(DefinitionDto("second"))).Data!.Id;
        }

        await using var context = CreateContext();

        // Act: rename "second" to the taken key "first"
        var result = await CreateRepository(context).UpdateDefinitionAsync(secondId, UpdateDto("first"));

        // Assert
        result.StatusCode.Should().Be(409);
        await using var verify = CreateContext();
        (await verify.Set<SettingDefinition>().SingleAsync(d => d.Id == secondId)).Key.Should().Be("second");
    }

    [Fact]
    public async Task CreateDefinitionAsync_WithOptionsAndRepeatedLevel_StoresEachLevelOnceAndReturnsDetails()
    {
        // Arrange
        var levelId = await SeedLevelAsync("System");
        await using var context = CreateContext();
        var dto = DefinitionDto("app.theme") with
        {
            Options = new[] { new CreateSettingOptionDto("dark", "Dark", 1, false, null), new CreateSettingOptionDto("light", "Light", 0, true, null) },
            AllowedLevelIds = new[] { levelId, levelId }
        };

        // Act
        var result = await CreateRepository(context).CreateDefinitionAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.AllowedLevelIds.Should().Equal(levelId);
        result.Data.Options.Select(o => o.Value).Should().Equal("light", "dark");
        result.Data.Options.Should().OnlyContain(o => o.SettingDefinitionId == result.Data.Id);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_Missing_ReturnsNotFoundWithSameMessage()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).UpdateDefinitionAsync(id, UpdateDto("x"));

        // Assert
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be($"Setting definition '{id}' not found");
    }

    [Fact]
    public async Task DeleteGroupAsync_KeepsDefinitionsWithoutGroup()
    {
        // Arrange
        Guid groupId;
        Guid definitionId;
        await using (var seed = CreateContext())
        {
            var repository = CreateRepository(seed);
            groupId = (await repository.CreateGroupAsync(new CreateSettingGroupDto("email", "Email", null, null, 0))).Data!.Id;
            definitionId = (await repository.CreateDefinitionAsync(DefinitionDto("email.host") with { GroupId = groupId })).Data!.Id;
        }

        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).DeleteGroupAsync(groupId);

        // Assert
        result.Success.Should().BeTrue();
        await using var verify = CreateContext();
        (await verify.Set<SettingGroup>().AnyAsync(g => g.Id == groupId)).Should().BeFalse();
        (await verify.Set<SettingDefinition>().SingleAsync(d => d.Id == definitionId)).GroupId.Should().BeNull();
    }

    [Fact]
    public async Task LevelQueries_ReportChildrenAndValues()
    {
        // Arrange
        var systemId = await SeedLevelAsync("System");
        var tenantId = await SeedLevelAsync("Tenant", systemId);
        await using var context = CreateContext();
        var sut = CreateRepository(context);

        // Act and assert
        (await sut.LevelNameExistsAsync("Tenant")).Should().BeTrue();
        (await sut.LevelNameExistsAsync("User")).Should().BeFalse();
        (await sut.LevelHasChildrenAsync(systemId)).Should().BeTrue();
        (await sut.LevelHasChildrenAsync(tenantId)).Should().BeFalse();
        (await sut.LevelHasValuesAsync(systemId)).Should().BeFalse();
        (await sut.GetLevelByIdAsync(tenantId))!.ParentId.Should().Be(systemId);
        (await sut.GetAllLevelsAsync()).Select(l => l.Name).Should().Equal("System", "Tenant");
    }

    private static SettingsAdminRepository CreateRepository(DbContext context)
        => new(context, SqliteUniqueConstraintDetector.Instance);

    private static CreateSettingDefinitionDto DefinitionDto(string key) => new(
        key, SettingDataType.String, null, null, key, null, null, null, 0,
        true, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null,
        Options: null, AllowedLevelIds: null);

    private static UpdateSettingDefinitionDto UpdateDto(string key) => new(
        key, SettingDataType.String, null, null, key, null, null, null, 0,
        true, false, false, false, false, false, null, null, null, null, null, null, null, null, null, null,
        Options: null, AllowedLevelIds: null);

    private async Task<Guid> SeedLevelAsync(string name, Guid? parentId = null)
    {
        await using var context = CreateContext();
        var level = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = parentId,
            DisplayOrder = parentId is null ? 0 : 1,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(level);
        await context.SaveChangesAsync();
        return level.Id;
    }

    private TestAdminDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestAdminDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TestAdminDbContext(options);
    }

    private sealed class TestAdminDbContext : GroundUpDbContext
    {
        public TestAdminDbContext(DbContextOptions<TestAdminDbContext> options)
            : base(options) { }
    }
}
