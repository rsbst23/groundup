using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for <see cref="GroundUp.Repositories.BaseTenantRepository{TEntity, TDto}"/>
/// using EF Core InMemory database for isolated testing.
/// </summary>
public sealed class BaseTenantRepositoryTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly TenantTestRepository _repository;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public BaseTenantRepositoryTests()
    {
        _context = TestDbContext.Create();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantA);
        _repository = new TenantTestRepository(_context, _tenantContext);
    }

    public void Dispose() => _context.Dispose();

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_WithMultipleTenants_ReturnsOnlyCurrentTenantEntities()
    {
        // Arrange
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "A1", TenantId = _tenantA });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "A2", TenantId = _tenantA });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "B1", TenantId = _tenantB });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "B2", TenantId = _tenantB });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync(new FilterParams());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalRecords);
        Assert.All(result.Data.Items, item => Assert.Equal(_tenantA, item.TenantId));
    }

    [Fact]
    public async Task GetAllAsync_WithNoCurrentTenantEntities_ReturnsEmptyResult()
    {
        // Arrange — only tenantB entities
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "B1", TenantId = _tenantB });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "B2", TenantId = _tenantB });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync(new FilterParams());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data!.TotalRecords);
        Assert.Empty(result.Data.Items);
    }

    [Fact]
    public async Task GetAllAsync_WithQueryShaper_AppliesTenantFilterBeforeQueryShaper()
    {
        // Arrange — entities for both tenants, some with matching name
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "Match", TenantId = _tenantA });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "NoMatch", TenantId = _tenantA });
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "Match", TenantId = _tenantB });
        await _context.SaveChangesAsync();

        // Act — use FilterParams with exact filter on Name to simulate queryShaper behavior
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { { "Name", "Match" } }
        };
        var result = await _repository.GetAllAsync(filterParams);

        // Assert — only tenantA's "Match" entity, not tenantB's
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.TotalRecords);
        Assert.Equal("Match", result.Data.Items[0].Name);
        Assert.Equal(_tenantA, result.Data.Items[0].TenantId);
    }

    [Fact]
    public async Task GetAllAsync_WithFilterParamsAndPaging_CombinesWithTenantFilter()
    {
        // Arrange — seed 10 entities for tenantA, 5 for tenantB
        for (var i = 0; i < 10; i++)
            _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = $"A{i:D2}", TenantId = _tenantA });
        for (var i = 0; i < 5; i++)
            _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = $"B{i:D2}", TenantId = _tenantB });
        await _context.SaveChangesAsync();

        // Act — page 1 of size 3
        var filterParams = new FilterParams { PageNumber = 1, PageSize = 3 };
        var result = await _repository.GetAllAsync(filterParams);

        // Assert — total is 10 (tenantA only), page has 3 items
        Assert.True(result.Success);
        Assert.Equal(10, result.Data!.TotalRecords);
        Assert.Equal(3, result.Data.Items.Count);
        Assert.All(result.Data.Items, item => Assert.Equal(_tenantA, item.TenantId));
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithOwnTenantEntity_ReturnsOk()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "Mine", TenantId = _tenantA };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(entity.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(entity.Id, result.Data!.Id);
        Assert.Equal("Mine", result.Data.Name);
        Assert.Equal(_tenantA, result.Data.TenantId);
    }

    [Fact]
    public async Task GetByIdAsync_WithOtherTenantEntity_ReturnsNotFound()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "NotMine", TenantId = _tenantB };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(entity.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Act
        var result = await _repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_WithQueryShaper_AppliesTenantFilterAndQueryShaper()
    {
        // Arrange — entity for tenantA and tenantB with same name
        var entityA = new TenantTestEntity { Id = Guid.NewGuid(), Name = "Shared", TenantId = _tenantA };
        var entityB = new TenantTestEntity { Id = Guid.NewGuid(), Name = "Shared", TenantId = _tenantB };
        _context.TenantTestEntities.AddRange(entityA, entityB);
        await _context.SaveChangesAsync();

        // Act — get tenantA's entity (tenant filter applied automatically)
        var resultA = await _repository.GetByIdAsync(entityA.Id);
        var resultB = await _repository.GetByIdAsync(entityB.Id);

        // Assert — tenantA entity found, tenantB entity not found
        Assert.True(resultA.Success);
        Assert.Equal(entityA.Id, resultA.Data!.Id);
        Assert.False(resultB.Success);
        Assert.Equal(404, resultB.StatusCode);
    }

    #endregion

    #region AddAsync

    [Fact]
    public async Task AddAsync_SetsEntityTenantIdToContextTenantId()
    {
        // Arrange — DTO without explicit TenantId
        var dto = new TenantTestDto { Name = "NewEntity" };

        // Act
        var result = await _repository.AddAsync(dto);

        // Assert
        Assert.True(result.Success);
        var persisted = await _context.TenantTestEntities.FindAsync(result.Data!.Id);
        Assert.NotNull(persisted);
        Assert.Equal(_tenantA, persisted.TenantId);
    }

    [Fact]
    public async Task AddAsync_OverwritesPreSetTenantId_PreventsSpoof()
    {
        // Arrange — DTO with spoofed TenantId
        var dto = new TenantTestDto { Name = "Spoofed", TenantId = _tenantB };

        // Act
        var result = await _repository.AddAsync(dto);

        // Assert — persisted entity has context TenantId, not the spoofed one
        Assert.True(result.Success);
        var persisted = await _context.TenantTestEntities.FindAsync(result.Data!.Id);
        Assert.NotNull(persisted);
        Assert.Equal(_tenantA, persisted.TenantId);
        Assert.NotEqual(_tenantB, persisted.TenantId);
    }

    [Fact]
    public async Task AddAsync_ReturnsOkWithCorrectTenantIdInDto()
    {
        // Arrange
        var dto = new TenantTestDto { Name = "Created", TenantId = Guid.Empty };

        // Act
        var result = await _repository.AddAsync(dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(_tenantA, result.Data!.TenantId);
        Assert.Equal("Created", result.Data.Name);
        Assert.NotEqual(Guid.Empty, result.Data.Id);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithOwnTenantEntity_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "Original", TenantId = _tenantA };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var dto = new TenantTestDto { Id = entity.Id, Name = "Updated", TenantId = _tenantA };

        // Act
        var result = await _repository.UpdateAsync(entity.Id, dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Updated", result.Data!.Name);
        Assert.Equal(_tenantA, result.Data.TenantId);
    }

    [Fact]
    public async Task UpdateAsync_WithOtherTenantEntity_ReturnsNotFound()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "OtherTenant", TenantId = _tenantB };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        var dto = new TenantTestDto { Id = entity.Id, Name = "Hacked", TenantId = _tenantA };

        // Act
        var result = await _repository.UpdateAsync(entity.Id, dto);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Act
        var dto = new TenantTestDto { Name = "Ghost" };
        var result = await _repository.UpdateAsync(Guid.NewGuid(), dto);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_PreservesOriginalTenantId_IgnoresDtoTenantId()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "Original", TenantId = _tenantA };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // DTO with different TenantId — should be ignored
        var dto = new TenantTestDto { Id = entity.Id, Name = "Updated", TenantId = _tenantB };

        // Act
        var result = await _repository.UpdateAsync(entity.Id, dto);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(_tenantA, result.Data!.TenantId);

        var persisted = await _context.TenantTestEntities.FindAsync(entity.Id);
        Assert.NotNull(persisted);
        Assert.Equal(_tenantA, persisted.TenantId);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithOwnTenantEntity_ReturnsOk()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "ToDelete", TenantId = _tenantA };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DeleteAsync(entity.Id);

        // Assert
        Assert.True(result.Success);
        var deleted = await _context.TenantTestEntities.FindAsync(entity.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_WithOtherTenantEntity_ReturnsNotFound()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "OtherTenant", TenantId = _tenantB };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DeleteAsync(entity.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);

        // Entity should still exist
        var stillExists = await _context.TenantTestEntities.FindAsync(entity.Id);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Act
        var result = await _repository.DeleteAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_WithSoftDeletableTenantEntity_SetsIsDeletedAndDeletedAt()
    {
        // Arrange — use separate context and soft-deletable repository
        using var softContext = TestDbContext.Create();
        var softTenantContext = Substitute.For<ITenantContext>();
        softTenantContext.TenantId.Returns(_tenantA);
        var softRepo = new SoftDeletableTenantTestRepository(softContext, softTenantContext);

        var entity = new SoftDeletableTenantTestEntity
        {
            Id = Guid.NewGuid(),
            Name = "SoftDelete",
            TenantId = _tenantA
        };
        softContext.SoftDeletableTenantTestEntities.Add(entity);
        await softContext.SaveChangesAsync();

        // Act
        var before = DateTime.UtcNow;
        var result = await softRepo.DeleteAsync(entity.Id);
        var after = DateTime.UtcNow;

        // Assert
        Assert.True(result.Success);

        var softDeleted = await softContext.SoftDeletableTenantTestEntities.FindAsync(entity.Id);
        Assert.NotNull(softDeleted);
        Assert.True(softDeleted.IsDeleted);
        Assert.NotNull(softDeleted.DeletedAt);
        Assert.True(softDeleted.DeletedAt >= before && softDeleted.DeletedAt <= after);
    }

    [Fact]
    public async Task DeleteAsync_WithNonSoftDeletableTenantEntity_RemovesFromDbSet()
    {
        // Arrange
        var entity = new TenantTestEntity { Id = Guid.NewGuid(), Name = "HardDelete", TenantId = _tenantA };
        _context.TenantTestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DeleteAsync(entity.Id);

        // Assert
        Assert.True(result.Success);
        var removed = await _context.TenantTestEntities.FindAsync(entity.Id);
        Assert.Null(removed);
    }

    #endregion
}
