using System.Linq.Expressions;
using GroundUp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories;

/// <summary>
/// The list pipeline: <see cref="FilterParams"/> filters, search, sorting, count, paging and a projection,
/// on any <see cref="IQueryable{T}"/>. <see cref="BaseRepository{TEntity, TDto}"/> uses it, and so can
/// repositories that build their own base query (for example one filtered by an explicit tenant id).
/// <para>
/// Filters and sorting run on the entity; the projection only shapes the page's rows, and EF translates
/// it to SQL. With a <see cref="ListFieldMap{TEntity}"/>, only its fields are used and any other name is
/// ignored; without one, names are entity property paths (see <see cref="ExpressionHelper"/>).
/// </para>
/// </summary>
public static class QueryableListExtensions
{
    /// <summary>
    /// Applies the filters and the search term of <paramref name="filters"/>.
    /// </summary>
    /// <param name="query">The query to filter.</param>
    /// <param name="filters">The filter parameters.</param>
    /// <param name="options">The field map; null for entity property paths.</param>
    public static IQueryable<T> ApplyListFilters<T>(
        this IQueryable<T> query, FilterParams filters, ListQueryOptions<T>? options = null)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var map = options?.Fields;

        foreach (var (key, value) in filters.Filters)
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildPredicate<T>(key, value))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildPredicate<T>(selector, value)) : query;
        }

        foreach (var (key, value) in filters.ContainsFilters)
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildContainsPredicate<T>(key, value))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildContainsPredicate<T>(selector, value)) : query;
        }

        foreach (var (key, value) in filters.StartsWithFilters)
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildStartsWithPredicate<T>(key, value))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildStartsWithPredicate<T>(selector, value)) : query;
        }

        foreach (var (key, value) in filters.EndsWithFilters)
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildEndsWithPredicate<T>(key, value))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildEndsWithPredicate<T>(selector, value)) : query;
        }

        // Range filters (min and max for the same field combine into one predicate)
        foreach (var key in filters.MinFilters.Keys.Union(filters.MaxFilters.Keys))
        {
            filters.MinFilters.TryGetValue(key, out var min);
            filters.MaxFilters.TryGetValue(key, out var max);
            query = map is null
                ? query.Where(ExpressionHelper.BuildRangePredicate<T>(key, min, max))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildRangePredicate<T>(selector, min, max)) : query;
        }

        foreach (var (key, values) in filters.MultiValueFilters)
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildMultiValuePredicate<T>(key, values))
                : map.TryGetFilter(key, out var selector) ? query.Where(ExpressionHelper.BuildMultiValuePredicate<T>(selector, values)) : query;
        }

        if (!string.IsNullOrWhiteSpace(filters.SearchTerm))
        {
            query = map is null
                ? query.Where(ExpressionHelper.BuildSearchPredicate<T>(filters.SearchTerm))
                : ApplySearch(query, map.SearchFields, filters.SearchTerm);
        }

        return query;
    }

    /// <summary>
    /// Orders the query by <paramref name="sortBy"/> (<c>"field"</c> or <c>"field desc"</c>, comma-separated),
    /// falling back to <see cref="ListQueryOptions{TEntity}.DefaultOrder"/> when no field is usable, then
    /// appends <see cref="ListQueryOptions{TEntity}.TieBreaker"/>. Without options it is
    /// <see cref="ExpressionHelper.ApplySorting{T}"/>.
    /// </summary>
    /// <param name="query">The query to order.</param>
    /// <param name="sortBy">The sort expression.</param>
    /// <param name="options">The field map, default order and tie-breaker.</param>
    public static IQueryable<T> ApplyListOrder<T>(
        this IQueryable<T> query, string? sortBy, ListQueryOptions<T>? options = null)
    {
        var ordered = false;
        var map = options?.Fields;

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            if (map is null)
            {
                var sorted = ExpressionHelper.ApplySorting(query, sortBy);
                ordered = !ReferenceEquals(sorted.Expression, query.Expression);
                query = sorted;
            }
            else
            {
                foreach (var column in sortBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = column.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0 || !map.TryGetSort(parts[0], out var selector))
                        continue;

                    var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                    query = ExpressionHelper.ApplyOrder(query, selector, descending, thenBy: ordered);
                    ordered = true;
                }
            }
        }

        if (!ordered && options?.DefaultOrder is not null)
        {
            query = options.DefaultOrder(query);
            ordered = true;
        }

        if (options?.TieBreaker is not null)
        {
            query = ExpressionHelper.ApplyOrder(query, options.TieBreaker, descending: false, thenBy: ordered);
        }

        return query;
    }

    /// <summary>
    /// Filters, orders, counts and pages the query, then projects the page's rows.
    /// </summary>
    /// <param name="query">The base query (already scoped, for example to a tenant).</param>
    /// <param name="filters">Filters, search, sort and paging.</param>
    /// <param name="projection">The row shape; translated by EF.</param>
    /// <param name="options">The field map, default order and tie-breaker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<PaginatedData<TResult>> ToPagedListAsync<T, TResult>(
        this IQueryable<T> query,
        FilterParams filters,
        Expression<Func<T, TResult>> projection,
        ListQueryOptions<T>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var filtered = query.ApplyListFilters(filters, options);
        var totalRecords = await filtered.CountAsync(cancellationToken);

        var page = filtered
            .ApplyListOrder(filters.SortBy, options)
            .Skip((filters.PageNumber - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .Select(projection);

        return new PaginatedData<TResult>
        {
            Items = await page.ToListAsync(cancellationToken),
            PageNumber = filters.PageNumber,
            PageSize = filters.PageSize,
            TotalRecords = totalRecords
        };
    }

    private static IQueryable<T> ApplySearch<T>(
        IQueryable<T> query, IReadOnlyList<Expression<Func<T, string?>>> fields, string searchTerm)
    {
        if (fields.Count == 0)
            return query;

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;
        foreach (var field in fields)
        {
            var contains = ExpressionHelper.BuildContainsPredicate<T>(field, searchTerm);
            var part = new ParameterReplacer(contains.Parameters[0], parameter).Visit(contains.Body);
            body = body is null ? part : Expression.OrElse(body, part);
        }

        return query.Where(Expression.Lambda<Func<T, bool>>(body!, parameter));
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
