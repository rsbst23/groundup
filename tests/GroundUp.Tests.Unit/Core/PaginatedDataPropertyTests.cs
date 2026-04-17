using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Unit.Core;

/// <summary>
/// Property-based tests for <see cref="PaginatedData{T}"/>.
/// Validates correctness properties from the Phase 1 design document.
/// </summary>
public sealed class PaginatedDataPropertyTests
{
    /// <summary>
    /// Property 6: PaginatedData computes TotalPages correctly.
    /// For any positive PageSize and non-negative TotalRecords,
    /// TotalPages == ⌈TotalRecords / PageSize⌉.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TotalPages_IsCeilingDivision(PositiveInt pageSize, NonNegativeInt totalRecords)
    {
        var data = new PaginatedData<string>
        {
            Items = new List<string>(),
            PageNumber = 1,
            PageSize = pageSize.Get,
            TotalRecords = totalRecords.Get
        };

        var expected = (int)Math.Ceiling((double)totalRecords.Get / pageSize.Get);
        return (data.TotalPages == expected).ToProperty();
    }

    /// <summary>
    /// When PageSize is 0, TotalPages should be 0 regardless of TotalRecords.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TotalPages_IsZero_WhenPageSizeIsZero(NonNegativeInt totalRecords)
    {
        var data = new PaginatedData<string>
        {
            Items = new List<string>(),
            PageNumber = 1,
            PageSize = 0,
            TotalRecords = totalRecords.Get
        };

        return (data.TotalPages == 0).ToProperty();
    }
}
