namespace GroundUp.Core.Models;

/// <summary>
/// Wraps a page of results with pagination metadata.
/// <see cref="TotalPages"/> is computed from <see cref="TotalRecords"/> and <see cref="PageSize"/>.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public sealed record PaginatedData<T>
{
    /// <summary>
    /// The items on this page.
    /// </summary>
    public required List<T> Items { get; init; }

    /// <summary>
    /// The 1-based page number of this result set.
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// The number of items requested per page.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// The total number of records across all pages.
    /// </summary>
    public required int TotalRecords { get; init; }

    /// <summary>
    /// The total number of pages, computed as ⌈TotalRecords / PageSize⌉.
    /// Returns 0 when PageSize is 0.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRecords / PageSize) : 0;
}
