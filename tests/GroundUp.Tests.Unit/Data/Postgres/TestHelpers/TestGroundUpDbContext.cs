using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Data.Postgres.TestHelpers;

/// <summary>
/// Concrete DbContext inheriting from <see cref="GroundUpDbContext"/> for unit testing.
/// Registers test entity DbSets and configures entity keys and constraints.
/// </summary>
public sealed class TestGroundUpDbContext : GroundUpDbContext
{
    public DbSet<AuditableTestEntity> AuditableTestEntities => Set<AuditableTestEntity>();
    public DbSet<SoftDeletableAuditableTestEntity> SoftDeletableAuditableTestEntities => Set<SoftDeletableAuditableTestEntity>();
    public DbSet<NonAuditableTestEntity> NonAuditableTestEntities => Set<NonAuditableTestEntity>();

    public TestGroundUpDbContext(DbContextOptions<TestGroundUpDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Call base first so GroundUpDbContext conventions (UUID v7, soft delete filters) apply
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditableTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<SoftDeletableAuditableTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<NonAuditableTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });
    }

    /// <summary>
    /// Creates a new TestGroundUpDbContext with a unique InMemory database for test isolation.
    /// </summary>
    public static TestGroundUpDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestGroundUpDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TestGroundUpDbContext(options);
    }
}
