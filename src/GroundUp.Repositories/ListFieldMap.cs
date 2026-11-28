using System.Linq.Expressions;

namespace GroundUp.Repositories;

/// <summary>
/// The fields a list accepts for sorting, filtering and search, by the names callers use (usually the
/// DTO's field names). Each name maps to an expression over the entity, so a field can be a navigation
/// (<c>x =&gt; x.User.Email</c>), a subquery, or a computed value. Names are matched case-insensitively.
/// A name that is not in the map is ignored by <see cref="QueryableListExtensions"/>.
/// </summary>
/// <typeparam name="TEntity">The entity the list queries.</typeparam>
public sealed class ListFieldMap<TEntity>
{
    private readonly Dictionary<string, LambdaExpression> _sort = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LambdaExpression> _filter = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Expression<Func<TEntity, string?>>> _search = new();

    /// <summary>The string fields the search term is matched against. Empty: search is ignored.</summary>
    public IReadOnlyList<Expression<Func<TEntity, string?>>> SearchFields => _search;

    /// <summary>Adds a field that can be sorted on.</summary>
    /// <param name="name">The name callers use, for example <c>email</c>.</param>
    /// <param name="selector">The value to sort on.</param>
    public ListFieldMap<TEntity> Sort<TValue>(string name, Expression<Func<TEntity, TValue>> selector)
    {
        _sort[Check(name)] = selector;
        return this;
    }

    /// <summary>Adds a field that can be filtered on (every <c>FilterParams</c> filter type).</summary>
    /// <param name="name">The name callers use, for example <c>status</c>.</param>
    /// <param name="selector">The value to filter on.</param>
    public ListFieldMap<TEntity> Filter<TValue>(string name, Expression<Func<TEntity, TValue>> selector)
    {
        _filter[Check(name)] = selector;
        return this;
    }

    /// <summary>Adds a field that can be sorted and filtered on.</summary>
    /// <param name="name">The name callers use.</param>
    /// <param name="selector">The value to sort and filter on.</param>
    public ListFieldMap<TEntity> SortAndFilter<TValue>(string name, Expression<Func<TEntity, TValue>> selector)
        => Sort(name, selector).Filter(name, selector);

    /// <summary>Sets the string fields the search term is matched against (case-insensitive substring).</summary>
    /// <param name="fields">The fields, for example <c>x =&gt; x.User.Email</c>.</param>
    public ListFieldMap<TEntity> Search(params Expression<Func<TEntity, string?>>[] fields)
    {
        _search.AddRange(fields);
        return this;
    }

    /// <summary>The sort selector for <paramref name="name"/>, if it is allowed.</summary>
    public bool TryGetSort(string name, out LambdaExpression selector)
        => _sort.TryGetValue(name, out selector!);

    /// <summary>The filter selector for <paramref name="name"/>, if it is allowed.</summary>
    public bool TryGetFilter(string name, out LambdaExpression selector)
        => _filter.TryGetValue(name, out selector!);

    private static string Check(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
