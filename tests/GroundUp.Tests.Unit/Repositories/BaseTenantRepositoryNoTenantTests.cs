using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Verifies that <see cref="GroundUp.Repositories.BaseTenantRepository{TEntity, TDto}"/> fails closed
/// (403 TENANT_REQUIRED) when there is no current tenant, for example a tenantless session.
/// </summary>
public sealed class BaseTenantRepositoryNoTenantTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly TenantTestRepository _repository;
    private readonly Guid _someTenant = Guid.NewGuid();

    public BaseTenantRepositoryNoTenantTests()
    {
        _context = TestDbContext.Create();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.Empty);
        _repository = new TenantTestRepository(_context, tenantContext);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetAllAsync_NoTenant_ReturnsForbiddenTenantRequired()
    {
        // Arrange — rows exist, but an empty tenant must not even attempt a query
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = Guid.NewGuid(), Name = "X", TenantId = _someTenant });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync(new FilterParams());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
    }

    [Fact]
    public async Task GetByIdAsync_NoTenant_ReturnsForbiddenTenantRequired()
    {
        // Arrange
        var id = Guid.NewGuid();
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = id, Name = "X", TenantId = _someTenant });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
    }

    [Fact]
    public async Task AddAsync_NoTenant_ReturnsForbiddenAndWritesNothing()
    {
        // Act
        var result = await _repository.AddAsync(new TenantTestDto { Name = "Orphan" });

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
        Assert.Empty(_context.TenantTestEntities);
    }

    [Fact]
    public async Task UpdateAsync_NoTenant_ReturnsForbiddenAndLeavesEntityUnchanged()
    {
        // Arrange
        var id = Guid.NewGuid();
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = id, Name = "Original", TenantId = _someTenant });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.UpdateAsync(id, new TenantTestDto { Id = id, Name = "Changed" });

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
        Assert.Equal("Original", (await _context.TenantTestEntities.FindAsync(id))!.Name);
    }

    [Fact]
    public async Task DeleteAsync_NoTenant_ReturnsForbiddenAndLeavesEntity()
    {
        // Arrange
        var id = Guid.NewGuid();
        _context.TenantTestEntities.Add(new TenantTestEntity { Id = id, Name = "Keep", TenantId = _someTenant });
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.DeleteAsync(id);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal(ErrorCodes.TenantRequired, result.ErrorCode);
        Assert.NotNull(await _context.TenantTestEntities.FindAsync(id));
    }
}
