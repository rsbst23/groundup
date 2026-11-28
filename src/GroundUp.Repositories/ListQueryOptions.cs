using System.Linq.Expressions;

namespace GroundUp.Repositories;

/// <summary>
/// How <see cref="QueryableListExtensions"/> reads <c>FilterParams</c> for one list.
/// Without options (or without <see cref="Fields"/>) names are entity property paths and the search
/// covers every string property, as in <see cref="BaseRepository{TEntity, TDto}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity the list queries.</typeparam>
public sealed class ListQueryOptions<TEntity>
{
    /// <summary>The allowed fields by caller name. Null: names are entity property paths.</summary>
    public ListFieldMap<TEntity>? Fields { get; init; }

    /// <summary>The order used when <c>SortBy</c> is empty or none of its fields is allowed.</summary>
    public Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? DefaultOrder { get; init; }

    /// <summary>
    /// A unique key appended to every order (for example <c>x =&gt; x.Id</c>), so rows with equal sort
    /// values keep one order and pages neither overlap nor skip rows.
    /// </summary>
    public Expression<Func<TEntity, Guid>>? TieBreaker { get; init; }
}
