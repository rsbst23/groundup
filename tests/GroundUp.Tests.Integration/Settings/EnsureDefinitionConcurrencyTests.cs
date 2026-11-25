using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using GroundUp.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using GroundUp.Repositories.Settings;

namespace GroundUp.Tests.Integration.Settings;

/// <summary>
/// Pins the behavior of <see cref="SettingsService.EnsureDefinitionAsync"/> under concurrency against
/// real Postgres: callers racing on one key all get the same definition, and only one row (with its
/// group and level links) is written. Only <see cref="CreateService"/> may change while GU-82 moves the
/// database access into a repository.
/// </summary>
public sealed class EnsureDefinitionConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private string _connectionString = null!;
    private Guid _systemLevelId;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var level = new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "System",
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
        context.Set<SettingLevel>().Add(level);
        await context.SaveChangesAsync();
        _systemLevelId = level.Id;
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task EnsureDefinitionAsync_ConcurrentCallsForSameKey_AllReturnSameDefinitionAndOneRowIsWritten()
    {
        // Arrange
        const int concurrency = 8;
        const string key = "test.concurrent.ensure";
        using var start = new SemaphoreSlim(0, concurrency);

        // Act: each caller has its own context, as separate requests or app instances would
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            await using var context = CreateContext();
            var service = CreateService(context);
            await start.WaitAsync();
            return await service.EnsureDefinitionAsync(CreateRequest(key));
        })).ToArray();

        start.Release(concurrency);
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r.Success, "every racing caller should get the definition");
        results.Select(r => r.Data!.Id).Distinct().Should().HaveCount(1, "all callers must see the same definition");

        await using var verify = CreateContext();
        var definitions = await verify.Set<SettingDefinition>().AsNoTracking()
            .Where(d => d.Key == key).ToListAsync();
        definitions.Should().HaveCount(1);
        definitions[0].Id.Should().Be(results[0].Data!.Id);

        (await verify.Set<SettingGroup>().AsNoTracking().CountAsync(g => g.Key == "test.concurrent.group"))
            .Should().Be(1);

        var links = await verify.Set<SettingDefinitionLevel>().AsNoTracking()
            .Where(l => l.SettingDefinitionId == definitions[0].Id).ToListAsync();
        links.Should().ContainSingle().Which.SettingLevelId.Should().Be(_systemLevelId);
    }

    [Fact]
    public async Task EnsureDefinitionAsync_ExistingKey_ReturnsExistingWithoutChangingIt()
    {
        // Arrange
        const string key = "test.existing.ensure";
        OperationResult<SettingDefinitionDto> first;
        await using (var context = CreateContext())
        {
            first = await CreateService(context).EnsureDefinitionAsync(CreateRequest(key));
        }

        // Act: a second call with a different display name must not overwrite the definition
        OperationResult<SettingDefinitionDto> second;
        await using (var context = CreateContext())
        {
            second = await CreateService(context).EnsureDefinitionAsync(
                CreateRequest(key) with { DisplayName = "Changed" });
        }

        // Assert
        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data!.Id.Should().Be(first.Data!.Id);
        second.Data.DisplayName.Should().Be("Concurrent Ensure");
    }

    private static EnsureSettingDefinitionRequest CreateRequest(string key) => new(
        Key: key,
        DataType: SettingDataType.String,
        DefaultValue: "default",
        DisplayName: "Concurrent Ensure",
        Description: null,
        Category: null,
        GroupKey: "test.concurrent.group",
        GroupDisplayName: "Concurrent Group",
        AllowedLevelNames: new[] { "system" });

    /// <summary>Builds the service under test over the given context.</summary>
    private static SettingsService CreateService(TestSettingsDbContext context)
    {
        var eventBus = new InProcessEventBus(new ServiceProviderStub(), NullLogger<InProcessEventBus>.Instance);
        return new SettingsService(
            new SettingsRepository(context, new PostgresUniqueConstraintDetector()),
            eventBus,
            new EmptyScopeChainProvider(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SettingsCacheOptions { CacheDuration = TimeSpan.Zero }),
            new SettingsCacheKeyTracker(),
            new AllowAllSettingsAuthorizer());
    }

    private TestSettingsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestSettingsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new TestSettingsDbContext(options);
    }

    private sealed class TestSettingsDbContext : GroundUpDbContext
    {
        public TestSettingsDbContext(DbContextOptions<TestSettingsDbContext> options)
            : base(options) { }
    }

    private sealed class EmptyScopeChainProvider : IScopeChainProvider
    {
        public Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SettingScopeEntry>>(Array.Empty<SettingScopeEntry>());
    }

    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
