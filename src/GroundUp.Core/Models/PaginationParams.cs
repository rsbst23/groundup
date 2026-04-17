namespace GroundUp.Core.Models;

/// <summary>
/// Carries pagination and sorting parameters for repository queries.
/// PageSize is capped at <see cref="DefaultMaxPageSize"/> (100).
/// PageNumber defaults to 1 if a value less than 1 is provided.
/// </summary>
public record PaginationParams
{
    /// <summary>
    /// The maximum allowed page size. PageSize values exceeding this are capped.
    /// </summary>
    public const int DefaultMaxPageSize = 100;

    private int _pageNumber = 1;
    private int _pageSize = 10;

    /// <summary>
    /// The 1-based page number. Values less than 1 are clamped to 1.
    /// </summary>
    public int PageNumber
    {
        get => _pageNumber;
        init => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>
    /// The number of items per page. Clamped to the range [1, <see cref="DefaultMaxPageSize"/>].
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value > DefaultMaxPageSize ? DefaultMaxPageSize : (value < 1 ? 1 : value);
    }

    /// <summary>
    /// Optional sort expression (e.g., "Name", "CreatedAt desc").
    /// </summary>
    public string? SortBy { get; init; }
}
