using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
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

    /// <summary>
    /// Builds a visibility queryShaper that restricts results to the current tenant
    /// and its direct children only.
    /// </summary>
    private Func<IQueryable<Tenant>, IQueryable<Tenant>> BuildVisibilityShaper()
    {
        var tenantId = _tenantContext.TenantId;
        return q => q.Where(t => t.Id == tenantId || t.ParentTenantId == tenantId);
    }
}
