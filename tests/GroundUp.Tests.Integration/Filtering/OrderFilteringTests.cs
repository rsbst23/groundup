using FluentAssertions;
using GroundUp.Core.Models;
using GroundUp.Sample.Data;
using GroundUp.Sample.Dtos;
using GroundUp.Sample.Repositories;
using GroundUp.Tests.Integration.Fixtures;

namespace GroundUp.Tests.Integration.Filtering;

/// <summary>
/// Integration tests for filtering, sorting, and paging across all supported data types.
/// Uses Testcontainers with real Postgres — each test gets its own isolated database.
/// </summary>
[Collection("Postgres")]
public sealed class OrderFilteringTests
{
    private readonly PostgresFixture _fixture;

    public OrderFilteringTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Creates a seeded context and repository pair for a single test.
    /// Each call creates a unique database for full test isolation.
    /// </summary>
    private (SampleDbContext Context, OrderRepository Repository) CreateSeededRepository()
    {
        var context = _fixture.CreateContext();
        OrderTestData.SeedTestOrders(context);
        var repository = new OrderRepository(context);
        return (context, repository);
    }

    #region Exact Match (Filters)

    [Fact]
    public async Task Filter_ByStatus_String_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["Status"] = "Shipped" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(3);
        result.Data.Items.Should().OnlyContain(o => o.Status == "Shipped");
    }

    [Fact]
    public async Task Filter_ByItemCount_Int_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["ItemCount"] = "5" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items.Single().OrderNumber.Should().Be("ORD-005");
    }

    [Fact]
    public async Task Filter_ByIsUrgent_Bool_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["IsUrgent"] = "true" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.IsUrgent);
    }

    [Fact]
    public async Task Filter_ByPriority_Enum_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["Priority"] = "High" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Select(o => o.OrderNumber).Should()
            .BeEquivalentTo(new[] { "ORD-004", "ORD-008" });
    }

    [Fact]
    public async Task Filter_ByCustomerId_Guid_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string>
            {
                ["CustomerId"] = OrderTestData.AliceCustomerId.ToString()
            },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.CustomerName == "Alice Corp");
    }

    #endregion

    #region Contains (ContainsFilters)

    [Fact]
    public async Task Filter_Contains_OrderNumber_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            ContainsFilters = new Dictionary<string, string> { ["OrderNumber"] = "005" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items.Single().OrderNumber.Should().Be("ORD-005");
    }

    #endregion

    #region StartsWith (StartsWithFilters)

    [Fact]
    public async Task Filter_StartsWith_OrderNumber_ReturnsMatchingOrders()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            StartsWithFilters = new Dictionary<string, string> { ["OrderNumber"] = "ORD-00" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // ORD-001 through ORD-009 start with "ORD-00", ORD-010 does not
        result.Data!.Items.Should().HaveCount(9);
        result.Data.Items.Should().OnlyContain(o => o.OrderNumber.StartsWith("ORD-00"));
    }

    #endregion

    #region Range (MinFilters / MaxFilters)

    [Fact]
    public async Task Filter_Range_Total_Decimal_MinAndMax()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["Total"] = "100" },
            MaxFilters = new Dictionary<string, string> { ["Total"] = "1000" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // Orders with Total between 100 and 1000: 150, 500, 800, 250, 400 = 5 orders
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.Total >= 100m && o.Total <= 1000m);
    }

    [Fact]
    public async Task Filter_Range_Total_Decimal_MinOnly()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["Total"] = "2000" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // Orders with Total >= 2000: 3000, 5000 = 2 orders
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().OnlyContain(o => o.Total >= 2000m);
    }

    [Fact]
    public async Task Filter_Range_Total_Decimal_MaxOnly()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MaxFilters = new Dictionary<string, string> { ["Total"] = "100" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // Orders with Total <= 100: 75, 50 = 2 orders
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Should().OnlyContain(o => o.Total <= 100m);
    }

    [Fact]
    public async Task Filter_Range_ItemCount_Int()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["ItemCount"] = "3" },
            MaxFilters = new Dictionary<string, string> { ["ItemCount"] = "7" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // ItemCount 3,4,5,6,7 = 5 orders
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.ItemCount >= 3 && o.ItemCount <= 7);
    }

    [Fact]
    public async Task Filter_Range_ShippingWeight_Double()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["ShippingWeight"] = "5.0" },
            MaxFilters = new Dictionary<string, string> { ["ShippingWeight"] = "15.0" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // ShippingWeight 5.0, 8.0, 10.0, 12.0, 15.0 = 5 orders
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.ShippingWeight >= 5.0 && o.ShippingWeight <= 15.0);
    }

    [Fact]
    public async Task Filter_Range_TrackingNumber_Long()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["TrackingNumber"] = "1000000003" },
            MaxFilters = new Dictionary<string, string> { ["TrackingNumber"] = "1000000007" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // TrackingNumber 3..7 = 5 orders
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o =>
            o.TrackingNumber >= 1000000003 && o.TrackingNumber <= 1000000007);
    }

    #endregion

    #region Date Range

    [Fact]
    public async Task Filter_DateRange_OrderDate_DateTime()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        // Npgsql requires UTC-aware DateTime for timestamp with time zone columns.
        // Use ISO 8601 with Z suffix so DateTime.Parse produces DateTimeKind.Utc.
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["OrderDate"] = "2026-03-01T00:00:00Z" },
            MaxFilters = new Dictionary<string, string> { ["OrderDate"] = "2026-06-30T23:59:59Z" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // March 5, April 20, May 1, June 15 = 4 orders
        result.Data!.Items.Should().HaveCount(4);
        result.Data.Items.Should().OnlyContain(o =>
            o.OrderDate >= new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            && o.OrderDate <= new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Filter_DateRange_ShipDate_DateOnly_Nullable()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["ShipDate"] = "2026-02-01" },
            MaxFilters = new Dictionary<string, string> { ["ShipDate"] = "2026-08-01" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // ShipDates in range: Feb 12, Mar 10, Jun 18, Jul 15 = 4 orders
        // (null ShipDates are excluded by the HasValue check in BuildRangePredicate)
        result.Data!.Items.Should().HaveCount(4);
        result.Data.Items.Should().OnlyContain(o => o.ShipDate.HasValue);
    }

    #endregion

    #region Nullable Range

    [Fact]
    public async Task Filter_Range_DiscountPercent_NullableInt()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MinFilters = new Dictionary<string, string> { ["DiscountPercent"] = "10" },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // DiscountPercent >= 10: 10, 15, 20, 25, 10 = 5 orders (nulls excluded)
        result.Data!.Items.Should().HaveCount(5);
        result.Data.Items.Should().OnlyContain(o => o.DiscountPercent.HasValue && o.DiscountPercent >= 10);
    }

    #endregion

    #region Multi-Value (MultiValueFilters)

    [Fact]
    public async Task Filter_MultiValue_Status_String()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            MultiValueFilters = new Dictionary<string, List<string>>
            {
                ["Status"] = new() { "Shipped", "Delivered" }
            },
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // Shipped: ORD-002, ORD-006, ORD-009 = 3; Delivered: ORD-003, ORD-007, ORD-010 = 3; total = 6
        result.Data!.Items.Should().HaveCount(6);
        result.Data.Items.Should().OnlyContain(o => o.Status == "Shipped" || o.Status == "Delivered");
    }

    #endregion

    #region Search (SearchTerm)

    [Fact]
    public async Task Filter_SearchTerm_MatchesAcrossStringProperties()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SearchTerm = "005",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        // "005" matches OrderNumber "ORD-005"
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items.Single().OrderNumber.Should().Be("ORD-005");
    }

    #endregion

    #region Sorting

    [Fact]
    public async Task Sort_ByTotal_Ascending()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "Total",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(10);
        result.Data.Items.Select(o => o.Total).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Sort_ByTotal_Descending()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "Total desc",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(10);
        result.Data.Items.Select(o => o.Total).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Sort_ByOrderDate_Ascending()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "OrderDate",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(10);
        result.Data.Items.Select(o => o.OrderDate).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Sort_MultiColumn_StatusThenTotal()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "Status, Total desc",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        var items = result.Data!.Items;
        items.Should().HaveCount(10);

        // Verify primary sort by Status ascending
        items.Select(o => o.Status).Should().BeInAscendingOrder();

        // Verify secondary sort: within each status group, Total should be descending
        var statusGroups = items.GroupBy(o => o.Status);
        foreach (var group in statusGroups)
        {
            group.Select(o => o.Total).Should().BeInDescendingOrder(
                because: $"orders within status '{group.Key}' should be sorted by Total descending");
        }
    }

    [Fact]
    public async Task Sort_ByItemCount_Int()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "ItemCount",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(10);
        result.Data.Items.Select(o => o.ItemCount).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Sort_ByIsUrgent_Bool()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "IsUrgent",
            PageSize = 100
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(10);
        // false (0) comes before true (1) in ascending order
        var urgentFlags = result.Data.Items.Select(o => o.IsUrgent).ToList();
        urgentFlags.Should().BeInAscendingOrder();
    }

    #endregion

    #region Paging

    [Fact]
    public async Task Paging_ReturnsCorrectPage()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            SortBy = "OrderNumber",
            PageNumber = 2,
            PageSize = 3
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(3);
        result.Data.TotalRecords.Should().Be(10);
        result.Data.PageNumber.Should().Be(2);
        result.Data.PageSize.Should().Be(3);
        result.Data.TotalPages.Should().Be(4); // ceil(10/3) = 4

        // Page 2 with sort by OrderNumber should be ORD-004, ORD-005, ORD-006
        result.Data.Items.Select(o => o.OrderNumber).Should()
            .ContainInOrder("ORD-004", "ORD-005", "ORD-006");
    }

    [Fact]
    public async Task Paging_TotalRecordsReflectsFilteredCount()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["Status"] = "Shipped" },
            SortBy = "OrderNumber",
            PageNumber = 1,
            PageSize = 2
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.TotalRecords.Should().Be(3); // 3 Shipped orders total
        result.Data.TotalPages.Should().Be(2); // ceil(3/2) = 2
    }

    #endregion

    #region Combined

    [Fact]
    public async Task Filter_And_Sort_And_Page_Combined()
    {
        // Arrange
        var (context, repo) = CreateSeededRepository();
        using var _ = context;
        var filterParams = new FilterParams
        {
            Filters = new Dictionary<string, string> { ["Status"] = "Shipped" },
            SortBy = "Total",
            PageNumber = 1,
            PageSize = 2
        };

        // Act
        var result = await repo.GetAllAsync(filterParams);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TotalRecords.Should().Be(3); // 3 Shipped orders
        result.Data.Items.Should().HaveCount(2); // Page size = 2
        result.Data.Items.Should().OnlyContain(o => o.Status == "Shipped");
        result.Data.Items.Select(o => o.Total).Should().BeInAscendingOrder();

        // Shipped orders sorted by Total asc: 75 (ORD-002), 3000 (ORD-006), 5000 (ORD-009)
        // Page 1 of 2 should be the first two
        result.Data.Items[0].Total.Should().Be(75.00m);
        result.Data.Items[1].Total.Should().Be(3000.00m);
    }

    #endregion
}
