using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Repositories.Mappers;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// Repository implementation for Permission entities. Extends BaseRepository with
/// custom lookup methods by permission key and module.
/// </summary>
public sealed class PermissionRepository : BaseRepository<Permission, PermissionDto>, IPermissionRepository
{
    /// <summary>
    /// Initializes a new instance of <see cref="PermissionRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public PermissionRepository(DbContext context)
        : base(context, AuthPermissionMapper.ToDto, AuthPermissionMapper.ToEntity)
    {
    }

    /// <inheritdoc />
    public async Task<OperationResult<PermissionDto>> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

        if (entity is null)
            return OperationResult<PermissionDto>.NotFound();

        return OperationResult<PermissionDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<PermissionDto>>> GetByModuleAsync(
        string module,
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Permission> query = DbSet.AsNoTracking()
            .Where(p => p.Module == module);

        // Apply filters and sorting
        query = ApplyFilterParams(query, filterParams);

        // Count after filtering, before paging
        var totalRecords = await query.CountAsync(cancellationToken);

        // Apply paging
        var items = await ApplyPaging(query, filterParams)
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var dtos = items.Select(MapToDto).ToList();

        var paginatedData = new PaginatedData<PermissionDto>
        {
            Items = dtos,
            PageNumber = filterParams.PageNumber,
            PageSize = filterParams.PageSize,
            TotalRecords = totalRecords
        };

        return OperationResult<PaginatedData<PermissionDto>>.Ok(paginatedData);
    }
}
