using GroundUp.Core.Entities;
using GroundUp.Data.Abstractions;
using GroundUp.Data.Postgres;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Unit tests for <see cref="MigrationStartupHostedService"/> and <see cref="PostgresDatabaseMigrator"/>.
/// Validates that migrations run during StartAsync.
/// Requirements: 5.9
/// </summary>
public sealed class MigrationStartupHostedServiceTests : IDisposable
{
    private readonly IDatabaseMigrator _migrator;
    private readonly ServiceProvider _serviceProvider;
    private readonly ILogger<MigrationStartupHostedService> _logger;
    private readonly MigrationStartupHostedService _sut;

    public MigrationStartupHostedServiceTests()
    {
        _migrator = Substitute.For<IDatabaseMigrator>();
        var services = new ServiceCollection();
        services.AddScoped(_ => _migrator);
        _serviceProvider = services.BuildServiceProvider();
        _logger = Substitute.For<ILogger<MigrationStartupHostedService>>();
        _sut = new MigrationStartupHostedService(_serviceProvider, _logger);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task StartAsync_RunsMigrations_LogsStartAndCompletion()
    {
        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Applying database migrations")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Database migrations complete")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task StartAsync_ResolvesMigratorFromScope_CallsMigrateAsyncOnce()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.StartAsync(cts.Token);

        // Assert
        await _migrator.Received(1).MigrateAsync(cts.Token);
    }

    [Fact]
    public async Task StartAsync_MigratorThrows_PropagatesAndSkipsCompletionLog()
    {
        // Arrange: a failed migration must stop startup, as before
        _migrator.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("migration failed")));

        // Act
        var exception = await Record.ExceptionAsync(() => _sut.StartAsync(CancellationToken.None));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Database migrations complete")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => _sut.StopAsync(CancellationToken.None));

        // Assert: StopAsync is a no-op that returns Task.CompletedTask
        Assert.Null(exception);
    }

    [Fact]
    public async Task PostgresDatabaseMigrator_MigrateAsync_AppliesModelOnRelationalProvider()
    {
        // Arrange: SQLite in-memory, since MigrateAsync requires a relational provider
        var options = new DbContextOptionsBuilder<TestMigrationDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        await using var context = new TestMigrationDbContext(options);
        var migrator = new PostgresDatabaseMigrator(context);

        // Act
        var exception = await Record.ExceptionAsync(() => migrator.MigrateAsync());

        // Assert: no exception means MigrateAsync ran against the context
        Assert.Null(exception);
    }

    /// <summary>
    /// Concrete DbContext for SQLite testing since GroundUpDbContext is abstract.
    /// </summary>
    private sealed class TestMigrationDbContext : GroundUpDbContext
    {
        public TestMigrationDbContext(DbContextOptions<TestMigrationDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Minimal configuration for SQLite provider
            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
            });
        }
    }
}
