using GroundUp.Core;
using GroundUp.Core.Entities;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories;

/// <summary>
/// Abstract base repository providing generic CRUD operations with filtering,
/// sorting, paging, and soft delete awareness. Derived repositories provide
/// Mapperly-generated mapping delegates via constructor parameters.
/// <para>
/// All public methods return <see cref="OperationResult{T}"/> or <see cref="OperationResult"/>.
/// Only <see cref="DbUpdateException"/> is caught (mapped to 409 Conflict).
/// All other exceptions propagate to the exception handling middleware.
/// </para>
/// <para>
/// Events are NOT published here — event publishing is the responsibility of
/// the service layer (BaseService) in Phase 3D.
/// </para>
/// </summary>
/// <typeparam name="TEntity">The EF Core entity type. Must extend <see cref="BaseEntity"/>.</typeparam>
/// <typeparam name="TDto">The DTO type exposed to the service layer.</typeparam>
public abstract class BaseRepository<TEntity, TDto> : IBaseRepository<TDto>
    where TEntity : BaseEntity
    where TDto : class
{
    /// <summary>
    /// The EF Core database context.
    /// </summary>
    protected DbContext Context { get; }

    /// <summary>
    /// The DbSet for the entity type. Available to derived repositories
    /// for custom query operations.
    /// </summary>
    protected DbSet<TEntity> DbSet { get; }

    private readonly Func<TEntity, TDto> _mapToDto;
    private readonly Func<TDto, TEntity> _mapToEntity;

    /// <summary>
    /// Initializes a new instance of <see cref="BaseRepository{TEntity, TDto}"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    /// <param name="mapToDto">Mapperly-generated entity-to-DTO mapping delegate.</param>
    /// <param name="mapToEntity">Mapperly-generated DTO-to-entity mapping delegate.</param>
    protected BaseRepository(
        DbContext context,
        Func<TEntity, TDto> mapToDto,
        Func<TDto, TEntity> mapToEntity)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
        _mapToDto = mapToDto;
        _mapToEntity = mapToEntity;
    }

    #region GetAllAsync

    /// <inheritdoc />
    public virtual async Task<OperationResult<PaginatedData<TDto>>> GetAllAsync(
        FilterParams filterParams,
        CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(filterParams, queryShaper: null, cancellationToken);
    }

    /// <summary>
    /// Retrieves a paginated, filtered, and sorted list of DTOs with optional query customization.
    /// Pipeline: AsNoTracking → QueryShaper → Filters → Sorting → Count → Paging → Map.
    /// </summary>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="queryShaper">Optional delegate to customize the query (e.g., Include navigation properties).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual async Task<OperationResult<PaginatedData<TDto>>> GetAllAsync(
        FilterParams filterParams,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = DbSet.AsNoTracking();

        // 1. QueryShaper (e.g., .Include() for navigation properties)
        if (queryShaper is not null)
            query = queryShaper(query);

        // 2. Apply all filters
        query = ApplyFilters(query, filterParams);

        // 3. Apply sorting
        if (!string.IsNullOrWhiteSpace(filterParams.SortBy))
            query = ExpressionHelper.ApplySorting(query, filterParams.SortBy);

        // 4. Count after filtering, before paging
        var totalRecords = await query.CountAsync(cancellationToken);

        // 5. Apply paging
        var items = await query
            .Skip((filterParams.PageNumber - 1) * filterParams.PageSize)
            .Take(filterParams.PageSize)
            .ToListAsync(cancellationToken);

        // 6. Map to DTOs
        var dtos = items.Select(_mapToDto).ToList();

        var paginatedData = new PaginatedData<TDto>
        {
            Items = dtos,
            PageNumber = filterParams.PageNumber,
            PageSize = filterParams.PageSize,
            TotalRecords = totalRecords
        };

        return OperationResult<PaginatedData<TDto>>.Ok(paginatedData);
    }

    #endregion

    #region GetByIdAsync

    /// <inheritdoc />
    public virtual async Task<OperationResult<TDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, queryShaper: null, cancellationToken);
    }

    /// <summary>
    /// Retrieves a single DTO by ID with optional query customization.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="queryShaper">Optional delegate to customize the query (e.g., Include navigation properties).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual async Task<OperationResult<TDto>> GetByIdAsync(
        Guid id,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = DbSet.AsNoTracking();

        if (queryShaper is not null)
            query = queryShaper(query);

        var entity = await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (entity is null)
            return OperationResult<TDto>.NotFound();

        return OperationResult<TDto>.Ok(_mapToDto(entity));
    }

    #endregion

    #region AddAsync

    /// <inheritdoc />
    public virtual async Task<OperationResult<TDto>> AddAsync(
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = _mapToEntity(dto);
            DbSet.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(_mapToDto(entity), "Created", 201);
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

    /// <inheritdoc />
    public virtual async Task<OperationResult<TDto>> UpdateAsync(
        Guid id,
        TDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

            if (entity is null)
                return OperationResult<TDto>.NotFound();

            // Map DTO values onto the tracked entity
            var updated = _mapToEntity(dto);
            Context.Entry(entity).CurrentValues.SetValues(updated);

            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(_mapToDto(entity));
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

    /// <inheritdoc />
    public virtual async Task<OperationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

        if (entity is null)
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

    #region Filter Application

    /// <summary>
    /// Applies all filter types from <see cref="FilterParams"/> to the query using
    /// <see cref="ExpressionHelper"/>. Called by <see cref="GetAllAsync(FilterParams, Func{IQueryable{TEntity}, IQueryable{TEntity}}?, CancellationToken)"/>.
    /// </summary>
    /// <param name="query">The queryable to filter.</param>
    /// <param name="filterParams">The filter parameters.</param>
    /// <returns>The filtered queryable.</returns>
    protected virtual IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> query, FilterParams filterParams)
    {
        // Exact-match filters
        foreach (var (key, value) in filterParams.Filters)
            query = query.Where(ExpressionHelper.BuildPredicate<TEntity>(key, value));

        // Contains (substring) filters
        foreach (var (key, value) in filterParams.ContainsFilters)
            query = query.Where(ExpressionHelper.BuildContainsPredicate<TEntity>(key, value));

        // StartsWith filters
        foreach (var (key, value) in filterParams.StartsWithFilters)
            query = query.Where(ExpressionHelper.BuildStartsWithPredicate<TEntity>(key, value));

        // EndsWith filters
        foreach (var (key, value) in filterParams.EndsWithFilters)
            query = query.Where(ExpressionHelper.BuildEndsWithPredicate<TEntity>(key, value));

        // Range filters (combine min and max for same property)
        var rangeProperties = filterParams.MinFilters.Keys
            .Union(filterParams.MaxFilters.Keys);
        foreach (var prop in rangeProperties)
        {
            filterParams.MinFilters.TryGetValue(prop, out var min);
            filterParams.MaxFilters.TryGetValue(prop, out var max);
            query = query.Where(ExpressionHelper.BuildRangePredicate<TEntity>(prop, min, max));
        }

        // Multi-value (IN clause) filters
        foreach (var (key, values) in filterParams.MultiValueFilters)
            query = query.Where(ExpressionHelper.BuildMultiValuePredicate<TEntity>(key, values));

        // Free-text search across all string properties
        if (!string.IsNullOrWhiteSpace(filterParams.SearchTerm))
            query = query.Where(ExpressionHelper.BuildSearchPredicate<TEntity>(filterParams.SearchTerm));

        return query;
    }

    #endregion

    #region Query Helpers (for derived repositories)

    /// <summary>
    /// Applies filter parameters and sorting to a query. Useful for derived repositories
    /// that need to build custom queries but still want standard filtering behavior.
    /// </summary>
    /// <param name="query">The queryable to filter and sort.</param>
    /// <param name="filterParams">The filter parameters.</param>
    /// <returns>The filtered and sorted queryable.</returns>
    protected IQueryable<TEntity> ApplyFilterParams(IQueryable<TEntity> query, FilterParams filterParams)
    {
        query = ApplyFilters(query, filterParams);
        if (!string.IsNullOrWhiteSpace(filterParams.SortBy))
            query = ExpressionHelper.ApplySorting(query, filterParams.SortBy);
        return query;
    }

    /// <summary>
    /// Applies paging (Skip/Take) to a query. Useful for derived repositories
    /// that build custom queries but still want standard paging.
    /// </summary>
    /// <typeparam name="TQuery">The queryable element type.</typeparam>
    /// <param name="query">The queryable to page.</param>
    /// <param name="filterParams">The pagination parameters.</param>
    /// <returns>The paged queryable.</returns>
    protected IQueryable<TQuery> ApplyPaging<TQuery>(IQueryable<TQuery> query, FilterParams filterParams)
    {
        return query
            .Skip((filterParams.PageNumber - 1) * filterParams.PageSize)
            .Take(filterParams.PageSize);
    }

    /// <summary>
    /// Maps an entity to a DTO using the Mapperly-generated mapping delegate.
    /// Available to derived repositories for custom query result mapping.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>The mapped DTO.</returns>
    protected TDto MapToDto(TEntity entity) => _mapToDto(entity);

    /// <summary>
    /// Maps a DTO to an entity using the Mapperly-generated mapping delegate.
    /// Available to derived repositories for custom operations.
    /// </summary>
    /// <param name="dto">The DTO to map.</param>
    /// <returns>The mapped entity.</returns>
    protected TEntity MapToEntity(TDto dto) => _mapToEntity(dto);

    #endregion
}
