using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for Tenant entities. Extends BaseRepository with
/// custom visibility enforcement that restricts queries to the current tenant
/// and its direct children only.
/// </summary>
public sealed class TenantRepository : BaseRepository<Tenant, TenantDto>, ITenantRepository
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of <see cref="TenantRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="tenantContext">Provides the current tenant identity for visibility enforcement.</param>
    public TenantRepository(DbContext context, ITenantContext tenantContext)
        : base(context, AuthTenantMapper.ToDto, AuthTenantMapper.ToEntity)
    {
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public override async Task<OperationResult<PaginatedData<TenantDto>>> GetAllAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(filterParams, BuildVisibilityShaper(), cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<OperationResult<TenantDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, BuildVisibilityShaper(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TenantDto>> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var entity = await DbSet.AsNoTracking()
            .Where(t => t.Id == tenantId || t.ParentTenantId == tenantId)
            .FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

        if (entity is null)
            return OperationResult<TenantDto>.NotFound();

        return OperationResult<TenantDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<TenantDto>>> GetChildTenantsAsync(
        Guid parentTenantId,
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        IQueryable<Tenant> query = DbSet.AsNoTracking()
            .Where(t => t.ParentTenantId == parentTenantId)
            .Where(t => t.Id == tenantId || t.ParentTenantId == tenantId);

        // Apply filters and sorting
        query = ApplyFilterParams(query, filterParams);

        // Count after filtering, before paging
        var totalRecords = await query.CountAsync(cancellationToken);

        // Apply paging
        var items = await ApplyPaging(query, filterParams)
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var dtos = items.Select(MapToDto).ToList();

        var paginatedData = new PaginatedData<TenantDto>
        {
            Items = dtos,
            PageNumber = filterParams.PageNumber,
            PageSize = filterParams.PageSize,
            TotalRecords = totalRecords
        };

        return OperationResult<PaginatedData<TenantDto>>.Ok(paginatedData);
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Uses <c>IgnoreQueryFilters()</c> to bypass tenant-context visibility,
    /// then explicitly excludes soft-deleted tenants. Required for host-based tenant resolution
    /// which runs before any tenant context is established.
    /// </remarks>
    public async Task<OperationResult<TenantDto>> GetBySlugBypassFilterAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.Set<Tenant>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

        if (entity is null)
            return OperationResult<TenantDto>.NotFound();

        return OperationResult<TenantDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    /// <remarks>
    /// SYSTEM BYPASS: Uses <c>IgnoreQueryFilters()</c> to bypass tenant-context visibility,
    /// then explicitly excludes soft-deleted tenants. Required for multi-tenant sign-in
    /// where the user must see their memberships' tenant details before selecting a tenant.
    /// </remarks>
    public async Task<OperationResult<List<TenantDto>>> GetByIdsBypassFilterAsync(
        IEnumerable<Guid> tenantIds,
        CancellationToken cancellationToken = default)
    {
        var idList = tenantIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return OperationResult<List<TenantDto>>.Ok(new List<TenantDto>());
        }

        var entities = await Context.Set<Tenant>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && idList.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var dtos = entities.Select(MapToDto).ToList();
        return OperationResult<List<TenantDto>>.Ok(dtos);
    }

    /// <summary>
    /// Builds a visibility queryShaper that restricts results to the current tenant
    /// and its direct children only.
    /// </summary>
    private Func<IQueryable<Tenant>, IQueryable<Tenant>> BuildVisibilityShaper()
    {
        var tenantId = _tenantContext.TenantId;
        return q => q.Where(t => t.Id == tenantId || t.ParentTenantId == tenantId);
    }

    /// <summary>
    /// Creates a new tenant. If ParentTenantId is set, verifies it matches the current tenant
    /// (only the current tenant can create children under itself). Top-level tenant creation
    /// (no parent) is allowed without restriction — this is a system-level operation.
    /// </summary>
    public override async Task<OperationResult<TenantDto>> AddAsync(
        TenantDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = MapToEntity(dto);

        // If creating a child tenant, enforce that ParentTenantId == current tenant
        if (entity.ParentTenantId.HasValue && entity.ParentTenantId.Value != _tenantContext.TenantId)
            return OperationResult<TenantDto>.NotFound();

        try
        {
            DbSet.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TenantDto>.Ok(MapToDto(entity), "Created", 201);
        }
        catch (DbUpdateException)
        {
            return OperationResult<TenantDto>.Fail(
                "A conflict occurred while saving the entity.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// Updates a tenant after verifying it is visible to the current tenant (self or child).
    /// Returns NotFound if the tenant is not within visibility scope.
    /// </summary>
    public override async Task<OperationResult<TenantDto>> UpdateAsync(
        Guid id,
        TenantDto dto,
        CancellationToken cancellationToken = default)
    {
        // Verify visibility — can only update self or direct children
        var existing = await GetByIdAsync(id, cancellationToken);
        if (!existing.Success)
            return OperationResult<TenantDto>.NotFound();

        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);
        if (entity is null)
            return OperationResult<TenantDto>.NotFound();

        var originalParentTenantId = entity.ParentTenantId;
        var updated = MapToEntity(dto);
        Context.Entry(entity).CurrentValues.SetValues(updated);

        // Preserve ParentTenantId — cannot be changed via update
        entity.ParentTenantId = originalParentTenantId;

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TenantDto>.Ok(MapToDto(entity));
        }
        catch (DbUpdateException)
        {
            return OperationResult<TenantDto>.Fail(
                "A conflict occurred while updating the entity.",
                409,
                ErrorCodes.Conflict);
        }
    }

    /// <summary>
    /// Soft-deletes a tenant after verifying it is visible to the current tenant (self or child).
    /// Returns NotFound if the tenant is not within visibility scope.
    /// </summary>
    public override async Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Verify visibility — can only delete self or direct children
        var existing = await GetByIdAsync(id, cancellationToken);
        if (!existing.Success)
            return OperationResult.NotFound();

        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);
        if (entity is null)
            return OperationResult.NotFound();

        // Use Remove() so the SoftDeleteInterceptor handles the conversion
        // (sets IsDeleted, DeletedAt, DeletedBy from ICurrentUser)
        DbSet.Remove(entity);

        await Context.SaveChangesAsync(cancellationToken);
        return OperationResult.Ok();
    }
}
