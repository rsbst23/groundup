using FluentAssertions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using GroundUp.Repositories.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories.Settings;

/// <summary>
/// Unit tests for the definition and value methods of <see cref="SettingsRepository"/> on SQLite
/// in-memory, including the unique-constraint paths of upsert and ensure-definition.
/// </summary>
public sealed class SettingsRepositoryRuntimeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _systemLevelId;
    private readonly Guid _tenantLevelId;

    public SettingsRepositoryRuntimeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var system = new SettingLevel { Id = Guid.NewGuid(), Name = "System", CreatedAt = DateTime.UtcNow };
        var tenant = new SettingLevel { Id = Guid.NewGuid(), Name = "Tenant", ParentId = system.Id, DisplayOrder = 1, CreatedAt = DateTime.UtcNow };
        context.Set<SettingLevel>().AddRange(system, tenant);
        context.SaveChanges();
        _systemLevelId = system.Id;
        _tenantLevelId = tenant.Id;
    }

    public void Dispose() => _connection.Dispose();

    #region Definitions

    [Fact]
    public async Task GetDefinitionByKeyAsync_Existing_IncludesAllowedLevelsAndOptions()
    {
        // Arrange
        var definitionId = await SeedDefinitionAsync("app.theme", withOption: true);
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetDefinitionByKeyAsync("app.theme");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(definitionId);
        result.AllowedLevelIds.Should().BeEquivalentTo(new[] { _systemLevelId, _tenantLevelId });
        result.Options.Should().ContainSingle().Which.Value.Should().Be("dark");
    }

    [Fact]
    public async Task GetDefinitionByKeyAsync_Unknown_ReturnsNull()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetDefinitionByKeyAsync("missing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDefinitionsByGroupAsync_ReturnsOnlyThatGroupsDefinitions()
    {
        // Arrange
        var groupId = await SeedGroupAsync("email");
        await SeedDefinitionAsync("email.host", groupId: groupId);
        await SeedDefinitionAsync("other.key");
        await using var context = CreateContext();
        var sut = CreateRepository(context);

        // Act
        var group = await sut.GetGroupByKeyAsync("email");
        var definitions = await sut.GetDefinitionsByGroupAsync(groupId);
        var all = await sut.GetAllDefinitionsAsync();

        // Assert
        group!.Id.Should().Be(groupId);
        definitions.Select(d => d.Key).Should().Equal("email.host");
        all.Select(d => d.Key).Should().BeEquivalentTo(new[] { "email.host", "other.key" });
    }

    #endregion

    #region EnsureDefinitionAsync

    [Fact]
    public async Task EnsureDefinitionAsync_New_CreatesGroupDefinitionAndLevelLinks()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).EnsureDefinitionAsync(EnsureRequest("auth.realm", "System", "tenant"));

        // Assert: level names match case-insensitively
        result.Success.Should().BeTrue();
        result.Data!.Key.Should().Be("auth.realm");
        result.Data.AllowedLevelIds.Should().BeEquivalentTo(new[] { _systemLevelId, _tenantLevelId });

        await using var verify = CreateContext();
        (await verify.Set<SettingGroup>().CountAsync(g => g.Key == "auth")).Should().Be(1);
        result.Data.GroupId.Should().Be((await verify.Set<SettingGroup>().SingleAsync(g => g.Key == "auth")).Id);
    }

    [Fact]
    public async Task EnsureDefinitionAsync_Existing_ReturnsItUnchanged()
    {
        // Arrange
        var definitionId = await SeedDefinitionAsync("auth.realm");
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).EnsureDefinitionAsync(
            EnsureRequest("auth.realm", "System") with { DisplayName = "Changed" });

        // Assert
        result.Data!.Id.Should().Be(definitionId);
        result.Data.DisplayName.Should().Be("auth.realm");
    }

    [Fact]
    public async Task EnsureDefinitionAsync_UniqueViolationAndNoWinner_Returns500AndClearsTracker()
    {
        // Arrange: every save fails as a unique violation, and no other caller created the definition
        var detector = Substitute.For<IUniqueConstraintDetector>();
        detector.IsUniqueConstraintViolation(Arg.Any<Exception>()).Returns(true);
        await using var context = CreateContext(new ThrowOnSaveInterceptor());

        // Act
        var result = await new SettingsRepository(context, detector).EnsureDefinitionAsync(EnsureRequest("auth.realm", "System"));

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Be("Failed to create setting definition 'auth.realm'");
        context.ChangeTracker.Entries().Should().BeEmpty("the failed attempt must not be retried by a later save");
    }

    [Fact]
    public async Task EnsureDefinitionAsync_OtherDatabaseError_Propagates()
    {
        // Arrange: the save fails, and it is not a unique violation
        var detector = Substitute.For<IUniqueConstraintDetector>();
        detector.IsUniqueConstraintViolation(Arg.Any<Exception>()).Returns(false);
        await using var context = CreateContext(new ThrowOnSaveInterceptor());

        // Act
        var act = () => new SettingsRepository(context, detector).EnsureDefinitionAsync(EnsureRequest("auth.realm", "System"));

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    #endregion

    #region Values

    [Fact]
    public async Task UpsertValueAsync_New_InsertsAndReturnsNullOldValue()
    {
        // Arrange
        var definitionId = await SeedDefinitionAsync("app.limit");
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).UpsertValueAsync(definitionId, _tenantLevelId, tenantId, "10");

        // Assert
        result.OldValue.Should().BeNull();
        result.Value.Value.Should().Be("10");
        result.Value.ScopeId.Should().Be(tenantId);
        (await ReadValuesAsync(definitionId)).Should().ContainSingle().Which.Value.Should().Be("10");
    }

    [Fact]
    public async Task UpsertValueAsync_Existing_UpdatesAndReturnsOldValue()
    {
        // Arrange
        var definitionId = await SeedDefinitionAsync("app.limit");
        var tenantId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await CreateRepository(context).UpsertValueAsync(definitionId, _tenantLevelId, tenantId, "10");
        }

        // Act
        SettingValueChangeDto result;
        await using (var context = CreateContext())
        {
            result = await CreateRepository(context).UpsertValueAsync(definitionId, _tenantLevelId, tenantId, "20");
        }

        // Assert
        result.OldValue.Should().Be("10");
        result.Value.Value.Should().Be("20");
        (await ReadValuesAsync(definitionId)).Should().ContainSingle().Which.Value.Should().Be("20");
    }

    [Fact]
    public async Task UpsertValueAsync_ConcurrentInsertOfSameValue_AppliesAsUpdate()
    {
        // Arrange: another caller inserts the same (definition, level, scope) after this caller's
        // lookup and before its save, so the insert hits the unique index
        var definitionId = await SeedDefinitionAsync("app.limit");
        var tenantId = Guid.NewGuid();
        var interceptor = new InsertCompetingValueOnSaveInterceptor(_connection, definitionId, _tenantLevelId, tenantId, "competing");
        await using var context = CreateContext(interceptor);

        // Act
        var result = await CreateRepository(context).UpsertValueAsync(definitionId, _tenantLevelId, tenantId, "mine");

        // Assert: one row, holding this caller's value; the old value is the competitor's
        interceptor.Inserted.Should().BeTrue();
        result.OldValue.Should().Be("competing");
        result.Value.Value.Should().Be("mine");
        (await ReadValuesAsync(definitionId)).Should().ContainSingle().Which.Value.Should().Be("mine");
    }

    [Fact]
    public async Task FindValueAsync_ReturnsValueWithDefinitionKey_AndDeleteRemovesIt()
    {
        // Arrange
        var definitionId = await SeedDefinitionAsync("app.limit");
        Guid valueId;
        await using (var context = CreateContext())
        {
            valueId = (await CreateRepository(context).UpsertValueAsync(definitionId, _systemLevelId, null, "5")).Value.Id;
        }

        await using var readContext = CreateContext();
        var sut = CreateRepository(readContext);

        // Act
        var found = await sut.FindValueAsync(valueId);
        var deleted = await sut.DeleteValueByIdAsync(valueId);
        var deletedAgain = await sut.DeleteValueByIdAsync(valueId);

        // Assert
        found!.DefinitionKey.Should().Be("app.limit");
        found.Value.Value.Should().Be("5");
        deleted.Should().BeTrue();
        deletedAgain.Should().BeFalse();
        (await sut.FindValueAsync(valueId)).Should().BeNull();
    }

    [Fact]
    public async Task GetValueAsync_And_GetValuesForDefinitionsAsync_FilterByKeys()
    {
        // Arrange
        var first = await SeedDefinitionAsync("a.key");
        var second = await SeedDefinitionAsync("b.key");
        var tenantId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            var writer = CreateRepository(context);
            await writer.UpsertValueAsync(first, _systemLevelId, null, "sys");
            await writer.UpsertValueAsync(first, _tenantLevelId, tenantId, "ten");
            await writer.UpsertValueAsync(second, _systemLevelId, null, "other");
        }

        await using var readContext = CreateContext();
        var sut = CreateRepository(readContext);

        // Act
        var tenantValue = await sut.GetValueAsync(first, _tenantLevelId, tenantId);
        var otherTenant = await sut.GetValueAsync(first, _tenantLevelId, Guid.NewGuid());
        var systemValue = await sut.GetValueAsync(first, _systemLevelId, null);
        var batch = await sut.GetValuesForDefinitionsAsync(new[] { first });

        // Assert
        tenantValue!.Value.Should().Be("ten");
        otherTenant.Should().BeNull();
        systemValue!.Value.Should().Be("sys");
        batch.Select(v => v.Value).Should().BeEquivalentTo(new[] { "sys", "ten" });
    }

    #endregion

    #region Helpers

    private static EnsureSettingDefinitionRequest EnsureRequest(string key, params string[] levelNames) => new(
        Key: key,
        DataType: SettingDataType.String,
        DefaultValue: "",
        DisplayName: key,
        Description: null,
        Category: null,
        GroupKey: "auth",
        GroupDisplayName: "Auth",
        AllowedLevelNames: levelNames);

    private static SettingsRepository CreateRepository(DbContext context)
        => new(context, SqliteUniqueConstraintDetector.Instance);

    private async Task<Guid> SeedGroupAsync(string key)
    {
        await using var context = CreateContext();
        var group = new SettingGroup { Id = Guid.NewGuid(), Key = key, DisplayName = key, CreatedAt = DateTime.UtcNow };
        context.Set<SettingGroup>().Add(group);
        await context.SaveChangesAsync();
        return group.Id;
    }

    private async Task<Guid> SeedDefinitionAsync(string key, Guid? groupId = null, bool withOption = false)
    {
        await using var context = CreateContext();
        var definition = new SettingDefinition
        {
            Id = Guid.NewGuid(),
            Key = key,
            DataType = SettingDataType.String,
            DisplayName = key,
            GroupId = groupId,
            CreatedAt = DateTime.UtcNow
        };
        definition.AllowedLevels.Add(new SettingDefinitionLevel { Id = Guid.NewGuid(), SettingDefinitionId = definition.Id, SettingLevelId = _systemLevelId });
        definition.AllowedLevels.Add(new SettingDefinitionLevel { Id = Guid.NewGuid(), SettingDefinitionId = definition.Id, SettingLevelId = _tenantLevelId });
        if (withOption)
        {
            definition.Options.Add(new SettingOption { Id = Guid.NewGuid(), SettingDefinitionId = definition.Id, Value = "dark", Label = "Dark" });
        }

        context.Set<SettingDefinition>().Add(definition);
        await context.SaveChangesAsync();
        return definition.Id;
    }

    private async Task<List<SettingValue>> ReadValuesAsync(Guid definitionId)
    {
        await using var context = CreateContext();
        return await context.Set<SettingValue>().AsNoTracking().Where(v => v.SettingDefinitionId == definitionId).ToListAsync();
    }

    private TestRuntimeDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TestRuntimeDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;
        return new TestRuntimeDbContext(options);
    }

    private sealed class TestRuntimeDbContext : GroundUpDbContext
    {
        public TestRuntimeDbContext(DbContextOptions<TestRuntimeDbContext> options)
            : base(options) { }
    }

    /// <summary>Fails every save with a <see cref="DbUpdateException"/>.</summary>
    private sealed class ThrowOnSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new DbUpdateException("simulated failure");
    }

    /// <summary>
    /// On the first save, inserts a competing value with the same (definition, level, scope) through a
    /// separate context, as a concurrent caller would.
    /// </summary>
    private sealed class InsertCompetingValueOnSaveInterceptor : SaveChangesInterceptor
    {
        private readonly SqliteConnection _connection;
        private readonly Guid _definitionId;
        private readonly Guid _levelId;
        private readonly Guid _scopeId;
        private readonly string _value;

        public InsertCompetingValueOnSaveInterceptor(
            SqliteConnection connection, Guid definitionId, Guid levelId, Guid scopeId, string value)
        {
            _connection = connection;
            _definitionId = definitionId;
            _levelId = levelId;
            _scopeId = scopeId;
            _value = value;
        }

        public bool Inserted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Inserted)
            {
                Inserted = true;
                var options = new DbContextOptionsBuilder<TestRuntimeDbContext>().UseSqlite(_connection).Options;
                await using var competitor = new TestRuntimeDbContext(options);
                competitor.Set<SettingValue>().Add(new SettingValue
                {
                    Id = Guid.NewGuid(),
                    SettingDefinitionId = _definitionId,
                    LevelId = _levelId,
                    ScopeId = _scopeId,
                    Value = _value,
                    CreatedAt = DateTime.UtcNow
                });
                await competitor.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    #endregion
}
