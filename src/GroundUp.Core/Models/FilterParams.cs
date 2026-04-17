namespace GroundUp.Core.Models;

/// <summary>
/// Extends <see cref="PaginationParams"/> with filtering capabilities:
/// exact match, contains, starts-with, ends-with, min/max range,
/// multi-value (IN clause), and free-text search.
/// </summary>
public sealed record FilterParams : PaginationParams
{
    /// <summary>
    /// Exact-match filters. Key = property name, Value = exact value to match.
    /// </summary>
    public Dictionary<string, string> Filters { get; init; } = new();

    /// <summary>
    /// Substring-match filters. Key = property name, Value = substring to search for.
    /// </summary>
    public Dictionary<string, string> ContainsFilters { get; init; } = new();

    /// <summary>
    /// Prefix-match filters. Key = property name, Value = prefix the property must start with.
    /// </summary>
    public Dictionary<string, string> StartsWithFilters { get; init; } = new();

    /// <summary>
    /// Suffix-match filters. Key = property name, Value = suffix the property must end with.
    /// </summary>
    public Dictionary<string, string> EndsWithFilters { get; init; } = new();

    /// <summary>
    /// Minimum range filters. Key = property name, Value = minimum value (inclusive).
    /// </summary>
    public Dictionary<string, string> MinFilters { get; init; } = new();

    /// <summary>
    /// Maximum range filters. Key = property name, Value = maximum value (inclusive).
    /// </summary>
    public Dictionary<string, string> MaxFilters { get; init; } = new();

    /// <summary>
    /// Multi-value filters (IN clause). Key = property name, Value = list of acceptable values.
    /// </summary>
    public Dictionary<string, List<string>> MultiValueFilters { get; init; } = new();

    /// <summary>
    /// Free-text search term applied across all string properties on the entity.
    /// Uses case-insensitive substring matching (contains).
    /// </summary>
    public string? SearchTerm { get; init; }
}
