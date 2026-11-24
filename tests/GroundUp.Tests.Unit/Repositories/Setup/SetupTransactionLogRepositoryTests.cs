using FluentAssertions;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using GroundUp.Repositories.Setup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories.Setup;

/// <summary>
/// Unit tests for <see cref="SetupTransactionLogRepository"/> on SQLite in-memory.
/// </summary>
public sealed class SetupTransactionLogRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<SetupTransactionLogRepository> _logger;

    public SetupTransactionLogRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _logger = Substitute.For<ILogger<SetupTransactionLogRepository>>();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    #region InsertWithRotationAsync

    [Fact]
    public async Task InsertWithRotationAsync_NewRow_PersistsFieldsAndReturnsId()
    {
        // Arrange
        await using var context = CreateContext();
        var sut = CreateRepository(context);

        // Act
        var id = await sut.InsertWithRotationAsync(
            new CreateSetupTransactionLogDto("first-admin-create", "keycloak-pending", "a@example.com", "corr-1"), 0);

        // Assert
        await using var verify = CreateContext();
        var row = await verify.SetupTransactionLogs.AsNoTracking().SingleAsync();
        row.Id.Should().Be(id);
        row.Operation.Should().Be("first-admin-create");
        row.Stage.Should().Be("keycloak-pending");
        row.Email.Should().Be("a@example.com");
        row.CorrelationId.Should().Be("corr-1");
        row.ExternalUserId.Should().BeNull();
        row.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task InsertWithRotationAsync_RotationDisabled_KeepsAllRows()
    {
        // Arrange
        await SeedAsync(("completed", -3), ("completed", -2), ("completed", -1));
        await using var context = CreateContext();

        // Act
        await CreateRepository(context).InsertWithRotationAsync(NewRow(), 0);

        // Assert
        await using var verify = CreateContext();
        (await verify.SetupTransactionLogs.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task InsertWithRotationAsync_OverLimit_RemovesOldestFinishedRowsOnly()
    {
        // Arrange: limit 3, four rows; the oldest is pending and must survive
        var ids = await SeedAsync(("db-pending", -4), ("completed", -3), ("failed", -2), ("completed", -1));
        await using var context = CreateContext();

        // Act
        await CreateRepository(context).InsertWithRotationAsync(NewRow(), 3);

        // Assert: total 4 + 1 - 3 = 2 to delete; the two oldest non-pending go
        await using var verify = CreateContext();
        var remaining = await verify.SetupTransactionLogs.AsNoTracking().Select(l => l.Id).ToListAsync();
        remaining.Should().HaveCount(3);
        remaining.Should().Contain(ids[0]).And.Contain(ids[3]);
        remaining.Should().NotContain(ids[1]).And.NotContain(ids[2]);
        _logger.DidNotReceive().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task InsertWithRotationAsync_PendingRowsBlockRotation_InsertsAndLogsWarning()
    {
        // Arrange: limit 1, all existing rows pending
        await SeedAsync(("keycloak-pending", -2), ("db-pending", -1));
        await using var context = CreateContext();

        // Act
        await CreateRepository(context).InsertWithRotationAsync(NewRow(), 1);

        // Assert
        await using var verify = CreateContext();
        (await verify.SetupTransactionLogs.CountAsync()).Should().Be(3);
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("could not free enough rows")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region UpdateStageAsync

    [Fact]
    public async Task UpdateStageAsync_WithExternalUserId_SetsStageAndExternalUserId()
    {
        // Arrange
        var ids = await SeedAsync(("keycloak-pending", -1));
        await using var context = CreateContext();

        // Act
        await CreateRepository(context).UpdateStageAsync(ids[0], "db-pending", externalUserId: "kc-123");

        // Assert
        var row = await ReadAsync(ids[0]);
        row.Stage.Should().Be("db-pending");
        row.ExternalUserId.Should().Be("kc-123");
        row.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStageAsync_NullOptionalFields_KeepsStoredValues()
    {
        // Arrange
        var ids = await SeedAsync(("keycloak-pending", -1));
        await using (var context = CreateContext())
        {
            await CreateRepository(context).UpdateStageAsync(ids[0], "db-pending", "kc-123", "first error");
        }

        // Act
        await using (var context = CreateContext())
        {
            await CreateRepository(context).UpdateStageAsync(ids[0], "completed");
        }

        // Assert
        var row = await ReadAsync(ids[0]);
        row.Stage.Should().Be("completed");
        row.ExternalUserId.Should().Be("kc-123");
        row.ErrorMessage.Should().Be("first error");
    }

    [Fact]
    public async Task UpdateStageAsync_UnknownId_DoesNothing()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var act = () => CreateRepository(context).UpdateStageAsync(Guid.NewGuid(), "failed", errorMessage: "x");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Queries

    [Fact]
    public async Task GetLatestUnfinishedFirstAdminIdAsync_ReturnsNewestPendingOrFailedFirstAdminRow()
    {
        // Arrange
        var ids = await SeedAsync(
            ("keycloak-pending", -5),
            ("failed", -4),
            ("completed", -1));
        await SeedAsync(operation: "other-operation", ("db-pending", 0));
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetLatestUnfinishedFirstAdminIdAsync();

        // Assert: the completed row and the other operation are ignored
        result.Should().Be(ids[1]);
    }

    [Fact]
    public async Task GetLatestUnfinishedFirstAdminIdAsync_NoUnfinishedRows_ReturnsNull()
    {
        // Arrange
        await SeedAsync(("completed", -1));
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetLatestUnfinishedFirstAdminIdAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirstUpToCount()
    {
        // Arrange
        var ids = await SeedAsync(("completed", -3), ("completed", -2), ("completed", -1));
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetRecentAsync(2);

        // Assert
        result.Select(r => r.Id).Should().Equal(ids[2], ids[1]);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingRow_ReturnsAllFields()
    {
        // Arrange
        var ids = await SeedAsync(("db-pending", -1));
        await using (var context = CreateContext())
        {
            await CreateRepository(context).UpdateStageAsync(ids[0], "failed", "kc-9", "boom");
        }

        await using var readContext = CreateContext();

        // Act
        var result = await CreateRepository(readContext).GetByIdAsync(ids[0]);

        // Assert
        result.Should().NotBeNull();
        result!.Operation.Should().Be("first-admin-create");
        result.Stage.Should().Be("failed");
        result.Email.Should().Be("seed@example.com");
        result.ExternalUserId.Should().Be("kc-9");
        result.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        // Arrange
        await using var context = CreateContext();

        // Act
        var result = await CreateRepository(context).GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static CreateSetupTransactionLogDto NewRow() => new("first-admin-create", "completed", null, null);

    private SetupTransactionLogRepository CreateRepository(DbContext context) => new(context, _logger);

    private Task<List<Guid>> SeedAsync(params (string Stage, int MinutesAgo)[] rows)
        => SeedAsync("first-admin-create", rows);

    private async Task<List<Guid>> SeedAsync(string operation, params (string Stage, int MinutesAgo)[] rows)
    {
        await using var context = CreateContext();
        var ids = new List<Guid>();
        foreach (var (stage, minutes) in rows)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            context.SetupTransactionLogs.Add(new SetupTransactionLog
            {
                Id = id,
                Operation = operation,
                Stage = stage,
                Email = "seed@example.com",
                CreatedAt = DateTime.UtcNow.AddMinutes(minutes),
                CreatedBy = "test"
            });
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private async Task<SetupTransactionLog> ReadAsync(Guid id)
    {
        await using var context = CreateContext();
        return await context.SetupTransactionLogs.AsNoTracking().SingleAsync(l => l.Id == id);
    }

    private TestSetupDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestSetupDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new TestSetupDbContext(options);
    }

    private sealed class TestSetupDbContext : GroundUpDbContext
    {
        public TestSetupDbContext(DbContextOptions<TestSetupDbContext> options)
            : base(options) { }
    }

    #endregion
}
