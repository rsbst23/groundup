using GroundUp.Data.Postgres;
using GroundUp.Sample.Entities;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Sample.Data;

public class SampleDbContext : GroundUpDbContext
{
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
        });
    }
}
