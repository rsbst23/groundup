using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Property-based tests for <see cref="GroundUp.Repositories.BaseTenantRepository{TEntity, TDto}"/>.
/// Validates tenant isolation invariants across randomized inputs.
/// </summary>
public sealed class BaseTenantRepositoryPropertyTests
{
    /// <summary>
    /// Property 1: GetAllAsync tenant isolation invariant.
    /// For any two distinct tenant IDs and any set of entities split across both tenants,
    /// GetAllAsync with ITenantContext.TenantId set to tenantA returns zero entities
    /// whose TenantId equals tenantB.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAllAsync_ReturnsZeroEntitiesFromOtherTenant(
        Guid tenantAId, Guid tenantBId, NonNull<string> nameA, NonNull<string> nameB)
    {
        // Ensure distinct tenants
        if (tenantAId == tenantBId) return true.ToProperty();

        return new Lazy<bool>(() =>
        {
            using var context = TestDbContext.Create();
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(tenantAId);
            var repo = new TenantTestRepository(context, tenantContext);

            // Seed entities for both tenants
            context.TenantTestEntities.Add(new TenantTestEntity
            {
                Id = Guid.NewGuid(),
                Name = nameA.Get,
                TenantId = tenantAId
            });
            context.TenantTestEntities.Add(new TenantTestEntity
            {
                Id = Guid.NewGuid(),
                Name = nameB.Get,
                TenantId = tenantBId
            });
            context.SaveChanges();

            var result = repo.GetAllAsync(new FilterParams()).GetAwaiter().GetResult();

            return result.Success
                && result.Data!.Items.All(dto => dto.TenantId == tenantAId)
                && result.Data.Items.All(dto => dto.TenantId != tenantBId);
        }).Value.ToProperty();
    }

    /// <summary>
    /// Property 2: GetByIdAsync cross-tenant access invariant.
    /// For any entity with a TenantId that differs from ITenantContext.TenantId,
    /// GetByIdAsync returns OperationResult.NotFound.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByIdAsync_CrossTenant_ReturnsNotFound(
        Guid tenantAId, Guid tenantBId, NonNull<string> name)
    {
        if (tenantAId == tenantBId) return true.ToProperty();

        return new Lazy<bool>(() =>
        {
            using var context = TestDbContext.Create();
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(tenantAId);
            var repo = new TenantTestRepository(context, tenantContext);

            // Seed entity belonging to tenantB
            var entityId = Guid.NewGuid();
            context.TenantTestEntities.Add(new TenantTestEntity
            {
                Id = entityId,
                Name = name.Get,
                TenantId = tenantBId
            });
            context.SaveChanges();

            var result = repo.GetByIdAsync(entityId).GetAwaiter().GetResult();

            return !result.Success && result.StatusCode == 404;
        }).Value.ToProperty();
    }

    /// <summary>
    /// Property 3: AddAsync tenant assignment invariant.
    /// For any DTO with any TenantId value (including Guid.Empty, random Guid, or current tenant Guid),
    /// AddAsync produces a persisted entity whose TenantId equals ITenantContext.TenantId.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AddAsync_AlwaysSetsEntityTenantIdToContextTenantId(
        Guid contextTenantId, Guid dtoTenantId, NonNull<string> name)
    {
        return new Lazy<bool>(() =>
        {
            using var context = TestDbContext.Create();
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(contextTenantId);
            var repo = new TenantTestRepository(context, tenantContext);

            var dto = new TenantTestDto
            {
                Name = name.Get,
                TenantId = dtoTenantId // Could be any value — should be overwritten
            };

            var result = repo.AddAsync(dto).GetAwaiter().GetResult();

            if (!result.Success) return false;

            // Verify persisted entity has the context tenant ID
            var persisted = context.TenantTestEntities.Find(result.Data!.Id);
            return persisted is not null
                && persisted.TenantId == contextTenantId
                && result.Data.TenantId == contextTenantId;
        }).Value.ToProperty();
    }

    /// <summary>
    /// Property 4: UpdateAsync TenantId preservation invariant.
    /// For any existing entity belonging to the current tenant and any DTO containing
    /// any TenantId value, UpdateAsync preserves the entity's original TenantId
    /// equal to ITenantContext.TenantId.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateAsync_PreservesOriginalTenantId(
        Guid contextTenantId, Guid dtoTenantId, NonNull<string> originalName, NonNull<string> updatedName)
    {
        return new Lazy<bool>(() =>
        {
            using var context = TestDbContext.Create();
            var tenantContext = Substitute.For<ITenantContext>();
            tenantContext.TenantId.Returns(contextTenantId);
            var repo = new TenantTestRepository(context, tenantContext);

            // Seed entity belonging to current tenant
            var entityId = Guid.NewGuid();
            context.TenantTestEntities.Add(new TenantTestEntity
            {
                Id = entityId,
                Name = originalName.Get,
                TenantId = contextTenantId
            });
            context.SaveChanges();

            // Update with DTO that has a different TenantId
            var dto = new TenantTestDto
            {
                Id = entityId,
                Name = updatedName.Get,
                TenantId = dtoTenantId // Should be ignored
            };

            var result = repo.UpdateAsync(entityId, dto).GetAwaiter().GetResult();

            if (!result.Success) return false;

            // Verify persisted entity still has the context tenant ID
            var persisted = context.TenantTestEntities.Find(entityId);
            return persisted is not null
                && persisted.TenantId == contextTenantId
                && result.Data!.TenantId == contextTenantId;
        }).Value.ToProperty();
    }
}
