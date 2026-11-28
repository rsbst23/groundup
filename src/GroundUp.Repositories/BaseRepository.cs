using System.Linq.Expressions;
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
/// Only <see cref="DbUpdateException"/> is caught (mapped to 409 Conflict). When an add or update
/// is rejected, the affected entity is detached so the <see cref="DbContext"/> stays usable for
/// later operations (for example a retry with different values).
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

    /// <summary>
    /// Stops tracking an entity whose save was rejected (for example by a unique constraint).
    /// Call this from every <see cref="DbUpdateException"/> handler. Otherwise the entity keeps its
    /// Added, Modified or Deleted state and every later SaveChanges on the same context retries the
    /// same failing statement, so the context cannot be reused (for example to retry with a new value).
    /// </summary>
    /// <param name="entity">The entity (or junction entity) whose pending change was rejected.</param>
    protected void DiscardRejectedChanges(object entity)
    {
        Context.Entry(entity).State = EntityState.Detached;
    }

    /// <summary>
    /// Restores the values an update must never take from a DTO: the creation audit fields
    /// (<see cref="IAuditable.CreatedAt"/>, <see cref="IAuditable.CreatedBy"/>) and the soft-delete
    /// fields (<see cref="ISoftDeletable"/>). DTOs do not carry them, so copying a mapped DTO onto a
    /// tracked entity would otherwise reset them (the creation time to <see cref="DateTime.MinValue"/>).
    /// Call it after <c>CurrentValues.SetValues</c> in every update. <see cref="IAuditable.UpdatedAt"/>
    /// and <see cref="IAuditable.UpdatedBy"/> are set by the save interceptor.
    /// </summary>
    /// <param name="entity">The tracked entity being updated.</param>
    protected void KeepManagedValues(object entity)
    {
        var entry = Context.Entry(entity);
        if (entity is IAuditable)
        {
            Restore(entry, nameof(IAuditable.CreatedAt));
            Restore(entry, nameof(IAuditable.CreatedBy));
        }

        if (entity is ISoftDeletable)
        {
            Restore(entry, nameof(ISoftDeletable.IsDeleted));
            Restore(entry, nameof(ISoftDeletable.DeletedAt));
            Restore(entry, nameof(ISoftDeletable.DeletedBy));
        }

        static void Restore(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string property)
        {
            var value = entry.Property(property);
            value.CurrentValue = value.OriginalValue;
            value.IsModified = false;
        }
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
        query = query.ApplyListOrder(filterParams.SortBy);

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

    /// <summary>
    /// Retrieves a page shaped by <paramref name="projection"/> instead of the mapper. Filters, search,
    /// sorting, count and paging run on the entity; the projection only shapes the page's rows and is
    /// translated by EF (see <see cref="QueryableListExtensions"/>).
    /// Pipeline: AsNoTracking → QueryShaper → Filters → Search → Sorting → Count → Paging → Projection.
    /// </summary>
    /// <typeparam name="TResult">The row type.</typeparam>
    /// <param name="filterParams">Filtering, sorting, and pagination parameters.</param>
    /// <param name="queryShaper">Optional delegate to customize the query before filtering.</param>
    /// <param name="projection">The row shape.</param>
    /// <param name="options">
    /// The allowed fields by caller name, the default order and the tie-breaker. Null: names are entity
    /// property paths and the search covers every string property.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected virtual async Task<OperationResult<PaginatedData<TResult>>> GetAllAsync<TResult>(
        FilterParams filterParams,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper,
        Expression<Func<TEntity, TResult>> projection,
        ListQueryOptions<TEntity>? options = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = DbSet.AsNoTracking();
        if (queryShaper is not null)
            query = queryShaper(query);

        var page = await query.ToPagedListAsync(filterParams, projection, options, cancellationToken);
        return OperationResult<PaginatedData<TResult>>.Ok(page);
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
        var entity = _mapToEntity(dto);

        try
        {
            DbSet.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(_mapToDto(entity), "Created", 201);
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(entity);

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
        var entity = await DbSet.FindAsync(new object[] { id }, cancellationToken);

        if (entity is null)
            return OperationResult<TDto>.NotFound();

        try
        {
            // Map DTO values onto the tracked entity
            var updated = _mapToEntity(dto);
            Context.Entry(entity).CurrentValues.SetValues(updated);
            KeepManagedValues(entity);

            await Context.SaveChangesAsync(cancellationToken);
            return OperationResult<TDto>.Ok(_mapToDto(entity));
        }
        catch (DbUpdateException)
        {
            DiscardRejectedChanges(entity);

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
        return query.ApplyListFilters(filterParams);
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
        return ApplyFilters(query, filterParams).ApplyListOrder(filterParams.SortBy);
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
