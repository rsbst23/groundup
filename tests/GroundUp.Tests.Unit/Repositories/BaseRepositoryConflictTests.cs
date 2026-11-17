using FluentAssertions;
using GroundUp.Core;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Tests that a failed save in <c>BaseRepository</c> leaves the <see cref="DbContext"/> usable.
/// Uses SQLite in-memory because the EF InMemory provider does not enforce unique indexes,
/// so it cannot produce the <see cref="DbUpdateException"/> these tests depend on.
/// </summary>
public sealed class BaseRepositoryConflictTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly UniqueNameTestDbContext _context;
    private readonly TestRepository _sut;

    public BaseRepositoryConflictTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new UniqueNameTestDbContext(options);
        _context.Database.EnsureCreated();
        _sut = new TestRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    #region AddAsync

    [Fact]
    public async Task AddAsync_UniqueConstraintViolation_ReturnsConflict()
    {
        // Arrange
        await _sut.AddAsync(CreateDto("taken"));

        // Act
        var result = await _sut.AddAsync(CreateDto("taken"));

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task AddAsync_AfterConflict_DetachesTheRejectedEntity()
    {
        // Arrange
        await _sut.AddAsync(CreateDto("taken"));

        // Act
        await _sut.AddAsync(CreateDto("taken"));

        // Assert — nothing is left queued for insert
        _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_AfterConflict_NextAddOnSameContextSucceeds()
    {
        // Arrange — the scenario behind slug retries: a rejected insert followed by a retry
        await _sut.AddAsync(CreateDto("taken"));
        await _sut.AddAsync(CreateDto("taken"));

        // Act
        var retry = await _sut.AddAsync(CreateDto("taken-2"));

        // Assert
        retry.Success.Should().BeTrue();
        (await _context.TestEntities.AsNoTracking().CountAsync()).Should().Be(2);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_UniqueConstraintViolation_ReturnsConflict()
    {
        // Arrange
        await _sut.AddAsync(CreateDto("first"));
        var second = (await _sut.AddAsync(CreateDto("second"))).Data!;

        // Act
        var result = await _sut.UpdateAsync(second.Id, CreateDto("first", second.Id));

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdateAsync_AfterConflict_NextAddOnSameContextSucceeds()
    {
        // Arrange
        await _sut.AddAsync(CreateDto("first"));
        var second = (await _sut.AddAsync(CreateDto("second"))).Data!;
        await _sut.UpdateAsync(second.Id, CreateDto("first", second.Id));

        // Act
        var next = await _sut.AddAsync(CreateDto("third"));

        // Assert
        next.Success.Should().BeTrue();
        _context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_AfterConflict_DatabaseKeepsOriginalValue()
    {
        // Arrange
        await _sut.AddAsync(CreateDto("first"));
        var second = (await _sut.AddAsync(CreateDto("second"))).Data!;

        // Act
        await _sut.UpdateAsync(second.Id, CreateDto("first", second.Id));
        var reloaded = await _sut.GetByIdAsync(second.Id);

        // Assert — the rejected update is not served from a stale tracked entity
        reloaded.Data!.Name.Should().Be("second");
    }

    #endregion

    #region BaseTenantRepository overrides

    [Fact]
    public async Task TenantRepositoryAddAsync_AfterConflict_NextAddOnSameContextSucceeds()
    {
        // Arrange
        var tenantRepository = new TenantTestRepository(_context, new TenantContext { TenantId = Guid.NewGuid() });
        await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "taken" });
        var conflict = await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "taken" });

        // Act
        var retry = await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "taken-2" });

        // Assert
        conflict.StatusCode.Should().Be(409);
        retry.Success.Should().BeTrue();
    }

    [Fact]
    public async Task TenantRepositoryUpdateAsync_AfterConflict_NextAddOnSameContextSucceeds()
    {
        // Arrange
        var tenantRepository = new TenantTestRepository(_context, new TenantContext { TenantId = Guid.NewGuid() });
        await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "first" });
        var second = (await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "second" })).Data!;
        var conflict = await tenantRepository.UpdateAsync(second.Id, new TenantTestDto { Id = second.Id, Name = "first" });

        // Act
        var next = await tenantRepository.AddAsync(new TenantTestDto { Id = Guid.NewGuid(), Name = "third" });

        // Assert
        conflict.StatusCode.Should().Be(409);
        next.Success.Should().BeTrue();
    }

    #endregion

    private static TestDto CreateDto(string name, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Score = 1,
        CreatedDate = DateTime.UtcNow
    };

    /// <summary>
    /// <see cref="TestDbContext"/> with unique indexes on the test entities' Name columns
    /// so duplicate inserts and updates raise <see cref="DbUpdateException"/>.
    /// </summary>
    private sealed class UniqueNameTestDbContext : TestDbContext
    {
        public UniqueNameTestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestEntity>().HasIndex(e => e.Name).IsUnique();
            modelBuilder.Entity<TenantTestEntity>().HasIndex(e => e.Name).IsUnique();
        }
    }
}
