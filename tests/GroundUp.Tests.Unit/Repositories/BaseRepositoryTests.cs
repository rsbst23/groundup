using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Repositories.TestHelpers;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for <see cref="GroundUp.Repositories.BaseRepository{TEntity, TDto}"/>
/// using EF Core InMemory database for isolated testing.
/// </summary>
public sealed class BaseRepositoryTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly TestRepository _repository;

    public BaseRepositoryTests()
    {
        _context = TestDbContext.Create();
        _repository = new TestRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_EntityExists_ReturnsOkWithDto()
    {
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Alice", Score = 42 };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByIdAsync(entity.Id);

        Assert.True(result.Success);
        Assert.Equal(entity.Id, result.Data!.Id);
        Assert.Equal("Alice", result.Data.Name);
        Assert.Equal(42, result.Data.Score);
    }

    [Fact]
    public async Task GetByIdAsync_EntityNotFound_ReturnsNotFound()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region AddAsync

    [Fact]
    public async Task AddAsync_ValidDto_PersistsAndReturnsOkWith201()
    {
        var dto = new TestDto { Name = "Bob", Score = 99 };

        var result = await _repository.AddAsync(dto);

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("Bob", result.Data!.Name);
        Assert.NotEqual(Guid.Empty, result.Data.Id);

        // Verify persisted
        var persisted = await _context.TestEntities.FindAsync(result.Data.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Bob", persisted.Name);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_EntityExists_AppliesChangesAndReturnsOk()
    {
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "Alice", Score = 10 };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var updatedDto = new TestDto { Id = entity.Id, Name = "Alice Updated", Score = 20 };
        var result = await _repository.UpdateAsync(entity.Id, updatedDto);

        Assert.True(result.Success);
        Assert.Equal("Alice Updated", result.Data!.Name);
        Assert.Equal(20, result.Data.Score);
    }

    [Fact]
    public async Task UpdateAsync_EntityNotFound_ReturnsNotFound()
    {
        var dto = new TestDto { Name = "Ghost" };
        var result = await _repository.UpdateAsync(Guid.NewGuid(), dto);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region DeleteAsync — Hard Delete

    [Fact]
    public async Task DeleteAsync_NonSoftDeletable_RemovesEntity()
    {
        var entity = new TestEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var result = await _repository.DeleteAsync(entity.Id);

        Assert.True(result.Success);

        var deleted = await _context.TestEntities.FindAsync(entity.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_EntityNotFound_ReturnsNotFound()
    {
        var result = await _repository.DeleteAsync(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region DeleteAsync — Soft Delete

    [Fact]
    public async Task DeleteAsync_SoftDeletable_SetsIsDeletedAndDeletedAt()
    {
        var softContext = TestDbContext.Create();
        var softRepo = new SoftDeletableTestRepository(softContext);

        var entity = new SoftDeletableTestEntity { Id = Guid.NewGuid(), Name = "SoftDelete" };
        softContext.SoftDeletableTestEntities.Add(entity);
        await softContext.SaveChangesAsync();

        var before = DateTime.UtcNow;
        var result = await softRepo.DeleteAsync(entity.Id);
        var after = DateTime.UtcNow;

        Assert.True(result.Success);

        var softDeleted = await softContext.SoftDeletableTestEntities.FindAsync(entity.Id);
        Assert.NotNull(softDeleted);
        Assert.True(softDeleted.IsDeleted);
        Assert.NotNull(softDeleted.DeletedAt);
        Assert.True(softDeleted.DeletedAt >= before && softDeleted.DeletedAt <= after);

        softContext.Dispose();
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ReturnsPagedResults()
    {
        for (var i = 0; i < 15; i++)
            _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = $"Item{i:D2}", Score = i });
        await _context.SaveChangesAsync();

        var filterParams = new FilterParams { PageNumber = 1, PageSize = 5 };
        var result = await _repository.GetAllAsync(filterParams);

        Assert.True(result.Success);
        Assert.Equal(5, result.Data!.Items.Count);
        Assert.Equal(15, result.Data.TotalRecords);
        Assert.Equal(3, result.Data.TotalPages);
        Assert.Equal(1, result.Data.PageNumber);
    }

    [Fact]
    public async Task GetAllAsync_WithExactFilter_FiltersResults()
    {
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Alice", Score = 10 });
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Bob", Score = 20 });
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Alice", Score = 30 });
        await _context.SaveChangesAsync();

        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { { "Name", "Alice" } }
        };
        var result = await _repository.GetAllAsync(filterParams);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalRecords);
        Assert.All(result.Data.Items, item => Assert.Equal("Alice", item.Name));
    }

    [Fact]
    public async Task GetAllAsync_WithSorting_SortsResults()
    {
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Charlie" });
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Alice" });
        _context.TestEntities.Add(new TestEntity { Id = Guid.NewGuid(), Name = "Bob" });
        await _context.SaveChangesAsync();

        var filterParams = new FilterParams { SortBy = "Name" };
        var result = await _repository.GetAllAsync(filterParams);

        Assert.True(result.Success);
        Assert.Equal("Alice", result.Data!.Items[0].Name);
        Assert.Equal("Bob", result.Data.Items[1].Name);
        Assert.Equal("Charlie", result.Data.Items[2].Name);
    }

    #endregion
}
