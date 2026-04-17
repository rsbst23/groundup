using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// EF Core DbContext for unit tests using InMemory database provider.
/// </summary>
public class TestDbContext : DbContext
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    public DbSet<SoftDeletableTestEntity> SoftDeletableTestEntities => Set<SoftDeletableTestEntity>();
    public DbSet<TenantTestEntity> TenantTestEntities => Set<TenantTestEntity>();
    public DbSet<SoftDeletableTenantTestEntity> SoftDeletableTenantTestEntities => Set<SoftDeletableTenantTestEntity>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<SoftDeletableTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<TenantTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<SoftDeletableTenantTestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });
    }

    /// <summary>
    /// Creates a new TestDbContext with a unique InMemory database for test isolation.
    /// </summary>
    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }
}
