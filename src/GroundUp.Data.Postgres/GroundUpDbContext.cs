using System.Linq.Expressions;
using GroundUp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Data.Postgres;

/// <summary>
/// Abstract base DbContext that consuming applications inherit from.
/// Configures UUID v7 default value generation for <see cref="BaseEntity.Id"/>,
/// applies global query filters for <see cref="ISoftDeletable"/> entities,
/// and calls base.OnModelCreating before applying framework conventions.
/// <para>
/// Consuming applications create their own DbContext inheriting from this class,
/// define their <c>DbSet&lt;T&gt;</c> properties, and apply entity configurations
/// using Fluent API in their overridden <c>OnModelCreating</c>.
/// </para>
/// </summary>
public abstract class GroundUpDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="GroundUpDbContext"/>.
    /// </summary>
    /// <param name="options">The DbContext options. Accepts the base type so derived
    /// contexts can pass their own typed options.</param>
    protected GroundUpDbContext(DbContextOptions options) : base(options) { }

    /// <summary>
    /// Configures framework conventions: UUID v7 value generation for BaseEntity.Id
    /// and global query filters for ISoftDeletable entities.
    /// Calls base.OnModelCreating first so derived context configurations register first.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. Call base so derived context entity configurations are registered first
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // 2. Configure UUID v7 default value generation for all BaseEntity.Id
            if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(BaseEntity.Id))
                    .HasValueGenerator<UuidV7ValueGenerator>();
            }

            // 3. Apply global query filters for ISoftDeletable entities
            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                ApplySoftDeleteFilter(modelBuilder, clrType);
            }
        }
    }

    /// <summary>
    /// Builds and applies a HasQueryFilter(e => !e.IsDeleted) expression
    /// dynamically for the given entity type using reflection.
    /// </summary>
    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
        var condition = Expression.Not(property);
        var lambda = Expression.Lambda(condition, parameter);

        modelBuilder.Entity(entityType).HasQueryFilter(lambda);
    }
}
