using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories;

/// <summary>
/// Abstract base repository that extends <see cref="BaseRepository{TEntity, TDto}"/>
/// with automatic tenant isolation. Every query is filtered by the current tenant,
/// and every mutation verifies tenant ownership before proceeding.
/// <para>
/// The generic constraint <c>where TEntity : BaseEntity, ITenantEntity</c> enforces
/// at compile time that only tenant-aware entities can be used — no runtime type checks.
/// </para>
/// <para>
/// Tenant filtering is applied via the queryShaper mechanism: a TenantShaper wraps
/// any derived class queryShaper, prepending a Where clause that filters by
/// <see cref="ITenantContext.TenantId"/>. This preserves the single-database-call
/// pipeline from BaseRepository.
/// </para>
/// <para>
/// Cross-tenant access returns NotFound (not Forbidden) to prevent information leakage
/// about entity existence in other tenants.
/// </para>
/// </summary>
/// <typeparam name="TEntity">The EF Core entity type. Must extend <see cref="BaseEntity"/>
/// and implement <see cref="ITenantEntity"/>.</typeparam>
/// <typeparam name="TDto">The DTO type exposed to the service layer.</typeparam>
public abstract class BaseTenantRepository<TEntity, TDto> : BaseRepository<TEntity, TDto>
    where TEntity : BaseEntity, ITenantEntity
    where TDto : class
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of <see cref="BaseTenantRepository{TEntity, TDto}"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for automatic filtering.</param>
    /// <param name="mapToDto">Mapperly-generated entity-to-DTO mapping delegate.</param>
    /// <param name="mapToEntity">Mapperly-generated DTO-to-entity mapping delegate.</param>
    protected BaseTenantRepository(
        DbContext context,
        ITenantContext tenantContext,
        Func<TEntity, TDto> mapToDto,
        Func<TDto, TEntity> mapToEntity)
        : base(context, mapToDto, mapToEntity)
    {
        _tenantContext = tenantContext;
    }

    #region GetAllAsync

    /// <summary>
    /// Retrieves a paginated, filtered, and sorted list of DTOs scoped to the current tenant.
    /// Composes a TenantShaper that applies the tenant filter before any derived class queryShaper.
    /// </summary>
    protected override async Task<OperationResult<PaginatedData<TDto>>> GetAllAsync(
        FilterParams filterParams,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper,
        CancellationToken cancellationToken = default)
    {
        var tenantShaper = ComposeTenantShaper(queryShaper);
        return await base.GetAllAsync(filterParams, tenantShaper, cancellationToken);
    }

    #endregion

    #region GetByIdAsync

    /// <summary>
    /// Retrieves a single DTO by ID scoped to the current tenant.
    /// If the entity exists but belongs to a different tenant, returns NotFound.
    /// </summary>
    protected override async Task<OperationResult<TDto>> GetByIdAsync(
        Guid id,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper,
        CancellationToken cancellationToken = default)
    {
        var tenantShaper = ComposeTenantShaper(queryShaper);
        return await base.GetByIdAsync(id, tenantShaper, cancellationToken);
    }

    #endregion

    #region AddAsync

    /// <summary>
    /// Creates a new entity with TenantId automatically set to the current tenant.
    /// Overwrites any pre-set TenantId to prevent tenant spoofing.
    /// </summary>
    public override async Task<OperationResult<TDto>> AddAsync(
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = MapToEntity(dto);
            entity.TenantId = _tenantContext.TenantId;
            DbSet.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(MapToDto(entity), "Created", 201);
        }
        catch (DbUpdateException)
        {
            return OperationResult<TDto>.Fail(
                "A conflict occurred while saving the entity.",
                409,
                ErrorCodes.Conflict);
        }
    }

    #endregion

    #region UpdateAsync

    /// <summary>
    /// Updates an existing entity after verifying it belongs to the current tenant.
    /// Returns NotFound if the entity does not exist or belongs to a different tenant.
    /// Preserves the original TenantId — it cannot be changed via the DTO.
    /// </summary>
    public override async Task<OperationResult<TDto>> UpdateAsync(
        Guid id,
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

            if (entity is null || entity.TenantId != _tenantContext.TenantId)
                return OperationResult<TDto>.NotFound();

            var updated = MapToEntity(dto);
            Context.Entry(entity).CurrentValues.SetValues(updated);

            // Preserve original TenantId — cannot be changed via DTO
            entity.TenantId = _tenantContext.TenantId;

            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(MapToDto(entity));
        }
        catch (DbUpdateException)
        {
            return OperationResult<TDto>.Fail(
                "A conflict occurred while updating the entity.",
                409,
                ErrorCodes.Conflict);
        }
    }

    #endregion

    #region DeleteAsync

    /// <summary>
    /// Deletes an entity after verifying it belongs to the current tenant.
    /// Returns NotFound if the entity does not exist or belongs to a different tenant.
    /// Performs soft delete for ISoftDeletable entities, hard delete otherwise.
    /// </summary>
    public override async Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

        if (entity is null || entity.TenantId != _tenantContext.TenantId)
            return OperationResult.NotFound();

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            var softDeletable = (ISoftDeletable)entity;
            softDeletable.IsDeleted = true;
            softDeletable.DeletedAt = DateTime.UtcNow;
        }
        else
        {
            DbSet.Remove(entity);
        }

        await Context.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok();
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Composes a TenantShaper that applies the tenant filter first,
    /// then delegates to the derived class queryShaper (if provided).
    /// Captures TenantId into a local variable to ensure it's evaluated once at call time.
    /// </summary>
    private Func<IQueryable<TEntity>, IQueryable<TEntity>> ComposeTenantShaper(
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper)
    {
        var tenantId = _tenantContext.TenantId;

        if (queryShaper is null)
            return q => q.Where(e => e.TenantId == tenantId);

        return q => queryShaper(q.Where(e => e.TenantId == tenantId));
    }

    #endregion
}
