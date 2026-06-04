using GroundUp.Core.Entities;
using GroundUp.Data.Postgres;
using GroundUp.Services.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Unit tests for <see cref="MigrationStartupHostedService"/>.
/// Validates that EF Core migrations run during StartAsync.
/// Requirements: 5.9
/// </summary>
public sealed class MigrationStartupHostedServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ILogger<MigrationStartupHostedService> _logger;
    private readonly MigrationStartupHostedService _sut;

    public MigrationStartupHostedServiceTests()
    {
        var services = new ServiceCollection();

        // Use SQLite in-memory since MigrateAsync requires a relational provider
        services.AddDbContext<GroundUpDbContext, TestMigrationDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"));

        _serviceProvider = services.BuildServiceProvider();
        _logger = Substitute.For<ILogger<MigrationStartupHostedService>>();
        _sut = new MigrationStartupHostedService(_serviceProvider, _logger);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task StartAsync_RunsMigrations_CompletesSuccessfully()
    {
        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert — service completed without throwing; both log messages were emitted
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
    public async Task StartAsync_ResolvesDbContextFromScope_CallsMigrateAsync()
    {
        // Act — verifies that the service creates a scope, resolves GroundUpDbContext,
        // and calls MigrateAsync (which applies the model to the SQLite database)
        var exception = await Record.ExceptionAsync(() => _sut.StartAsync(CancellationToken.None));

        // Assert — no exception means scope creation, DbContext resolution, and MigrateAsync all succeeded
        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_CompletesImmediately()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => _sut.StopAsync(CancellationToken.None));

        // Assert — StopAsync is a no-op that returns Task.CompletedTask
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
