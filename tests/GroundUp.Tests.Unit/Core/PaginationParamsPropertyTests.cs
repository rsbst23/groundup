using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Unit.Core;

/// <summary>
/// Property-based tests for <see cref="PaginationParams"/>.
/// Validates correctness properties from the Phase 1 design document.
/// </summary>
public sealed class PaginationParamsPropertyTests
{
    /// <summary>
    /// Property 5: PaginationParams clamps values to valid ranges.
    /// For any integer PageNumber, the result is always >= 1.
    /// For any integer PageSize, the result is always in [1, 100].
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PageNumber_IsAlwaysAtLeast1(int pageNumber)
    {
        var p = new PaginationParams { PageNumber = pageNumber };
        return (p.PageNumber >= 1).ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property PageSize_IsAlwaysInValidRange(int pageSize)
    {
        var p = new PaginationParams { PageSize = pageSize };
        return (p.PageSize >= 1 && p.PageSize <= PaginationParams.DefaultMaxPageSize).ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property PageSize_PreservesValidValues(PositiveInt pageSize)
    {
        var clamped = Math.Min(pageSize.Get, PaginationParams.DefaultMaxPageSize);
        var p = new PaginationParams { PageSize = pageSize.Get };
        return (p.PageSize == clamped).ToProperty();
    }
}
