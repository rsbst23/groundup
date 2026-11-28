using System.Linq.Expressions;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for the projected <c>GetAllAsync&lt;TResult&gt;</c> overload on
/// <see cref="BaseRepository{TEntity, TDto}"/> and <see cref="BaseTenantRepository{TEntity, TDto}"/>.
/// </summary>
public sealed class BaseRepositoryProjectionTests : IDisposable
{
    private readonly TestDbContext _context = TestDbContext.Create();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public BaseRepositoryProjectionTests()
    {
        _tenantContext.TenantId.Returns(_tenantA);
    }

    public void Dispose() => _context.Dispose();

    private sealed class ProjectingRepository(TestDbContext context) : TestRepository(context)
    {
        public Task<OperationResult<PaginatedData<string>>> NamesAsync(
            FilterParams filters,
            Func<IQueryable<TestEntity>, IQueryable<TestEntity>>? shaper = null,
            ListQueryOptions<TestEntity>? options = null)
            => GetAllAsync(filters, shaper, x => x.Name + "!", options);
    }

    private sealed class ProjectingTenantRepository(TestDbContext context, ITenantContext tenantContext)
        : TenantTestRepository(context, tenantContext)
    {
        public Task<OperationResult<PaginatedData<string>>> NamesAsync(
            FilterParams filters,
            Func<IQueryable<TenantTestEntity>, IQueryable<TenantTestEntity>>? shaper = null)
            => GetAllAsync(filters, shaper, (Expression<Func<TenantTestEntity, string>>)(x => x.Name));
    }

    [Fact]
    public async Task GetAllAsyncProjected_AppliesShaperFiltersAndProjection()
    {
        _context.TestEntities.AddRange(
            new TestEntity { Id = Guid.NewGuid(), Name = "Keep", Score = 10 },
            new TestEntity { Id = Guid.NewGuid(), Name = "Also", Score = 20 },
            new TestEntity { Id = Guid.NewGuid(), Name = "Hidden", Score = 30 });
        await _context.SaveChangesAsync();
        var repository = new ProjectingRepository(_context);
        var options = new ListQueryOptions<TestEntity>
        {
            Fields = new ListFieldMap<TestEntity>().Sort("points", x => x.Score),
            TieBreaker = x => x.Id
        };

        var result = await repository.NamesAsync(
            new FilterParams { SortBy = "points desc" },
            q => q.Where(x => x.Name != "Hidden"),
            options);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalRecords);
        Assert.Equal(new[] { "Also!", "Keep!" }, result.Data.Items);
    }

    [Fact]
    public async Task GetAllAsyncProjected_WithoutOptions_UsesEntityPropertyPaths()
    {
        _context.TestEntities.AddRange(
            new TestEntity { Id = Guid.NewGuid(), Name = "Alpha", Score = 10 },
            new TestEntity { Id = Guid.NewGuid(), Name = "Beta", Score = 20 });
        await _context.SaveChangesAsync();
        var repository = new ProjectingRepository(_context);

        var result = await repository.NamesAsync(new FilterParams { Filters = new() { ["Name"] = "beta" } });

        Assert.Equal(new[] { "Beta!" }, result.Data!.Items);
    }

    [Fact]
    public async Task GetAllAsyncProjected_TenantRepository_ReturnsOnlyCurrentTenantBeforeShaper()
    {
        _context.TenantTestEntities.AddRange(
            new TenantTestEntity { Id = Guid.NewGuid(), Name = "A1", TenantId = _tenantA },
            new TenantTestEntity { Id = Guid.NewGuid(), Name = "A2", TenantId = _tenantA },
            new TenantTestEntity { Id = Guid.NewGuid(), Name = "B1", TenantId = _tenantB });
        await _context.SaveChangesAsync();
        var repository = new ProjectingTenantRepository(_context, _tenantContext);

        // The shaper would let tenant B's row through; the tenant filter still applies
        var result = await repository.NamesAsync(new FilterParams(), q => q.Where(x => x.Name != "A2" || x.Name == "B1"));

        Assert.True(result.Success);
        Assert.Equal(new[] { "A1" }, result.Data!.Items);
    }

    [Fact]
    public async Task GetAllAsyncProjected_TenantRepositoryWithoutTenant_ReturnsTenantRequired()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);
        var repository = new ProjectingTenantRepository(_context, _tenantContext);

        var result = await repository.NamesAsync(new FilterParams());

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
    }
}
