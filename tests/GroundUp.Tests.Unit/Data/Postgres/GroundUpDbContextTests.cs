using GroundUp.Data.Postgres;
using GroundUp.Tests.Unit.Data.Postgres.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Data.Postgres;

/// <summary>
/// Unit tests for <see cref="GroundUpDbContext"/> conventions:
/// UUID v7 value generation, soft delete query filters.
/// </summary>
public sealed class GroundUpDbContextTests
{
    [Fact]
    public void OnModelCreating_BaseEntity_ConfiguresUuidV7ValueGenerator()
    {
        // Arrange
        using var context = TestGroundUpDbContext.Create();

        // Act
        var entityType = context.Model.FindEntityType(typeof(AuditableTestEntity));
        var idProperty = entityType!.FindProperty(nameof(AuditableTestEntity.Id));
        var valueGeneratorFactory = idProperty!.GetValueGeneratorFactory();

        // Assert — value generator factory should produce a UuidV7ValueGenerator
        Assert.NotNull(valueGeneratorFactory);
        var generator = valueGeneratorFactory!(idProperty, entityType);
        Assert.IsType<UuidV7ValueGenerator>(generator);
    }

    [Fact]
    public void OnModelCreating_SoftDeletableEntity_AppliesQueryFilter()
    {
        // Arrange
        using var context = TestGroundUpDbContext.Create();

        // Act
        var entityType = context.Model.FindEntityType(typeof(SoftDeletableAuditableTestEntity));
        var queryFilter = entityType!.GetQueryFilter();

        // Assert
        Assert.NotNull(queryFilter);
    }

    [Fact]
    public void OnModelCreating_NonSoftDeletableEntity_NoQueryFilter()
    {
        // Arrange
        using var context = TestGroundUpDbContext.Create();

        // Act
        var entityType = context.Model.FindEntityType(typeof(NonAuditableTestEntity));
        var queryFilter = entityType!.GetQueryFilter();

        // Assert
        Assert.Null(queryFilter);
    }

    [Fact]
    public async Task OnModelCreating_QueryExcludesSoftDeletedEntities()
    {
        // Arrange
        using var context = TestGroundUpDbContext.Create();
        context.SoftDeletableAuditableTestEntities.Add(new SoftDeletableAuditableTestEntity
        {
            Name = "Active",
            IsDeleted = false
        });
        context.SoftDeletableAuditableTestEntities.Add(new SoftDeletableAuditableTestEntity
        {
            Name = "Deleted",
            IsDeleted = true
        });
        await context.SaveChangesAsync();

        // Act
        var results = await context.SoftDeletableAuditableTestEntities.ToListAsync();

        // Assert
        Assert.Single(results);
        Assert.Equal("Active", results[0].Name);
    }

    [Fact]
    public async Task OnModelCreating_IgnoreQueryFilters_ReturnsSoftDeletedEntities()
    {
        // Arrange
        using var context = TestGroundUpDbContext.Create();
        context.SoftDeletableAuditableTestEntities.Add(new SoftDeletableAuditableTestEntity
        {
            Name = "Active",
            IsDeleted = false
        });
        context.SoftDeletableAuditableTestEntities.Add(new SoftDeletableAuditableTestEntity
        {
            Name = "Deleted",
            IsDeleted = true
        });
        await context.SaveChangesAsync();

        // Act
        var results = await context.SoftDeletableAuditableTestEntities
            .IgnoreQueryFilters()
            .ToListAsync();

        // Assert
        Assert.Equal(2, results.Count);
    }
}
