using GroundUp.Core.Models;
using GroundUp.Repositories;
using GroundUp.Tests.Unit.Repositories.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for <see cref="QueryableListExtensions"/>: the list pipeline with and without a field map.
/// Runs on the in-memory provider (logic only); SQL translation is covered by the Postgres integration tests.
/// </summary>
public sealed class QueryableListExtensionsTests : IDisposable
{
    /// <summary>A computed value used as a filter and sort field.</summary>
    public enum Band
    {
        Low = 0,
        High = 1
    }

    private readonly TestDbContext _context = TestDbContext.Create();

    public void Dispose() => _context.Dispose();

    private static ListQueryOptions<TestEntity> Options(bool withSearch = true) => new()
    {
        Fields = Map(withSearch),
        DefaultOrder = q => q.OrderByDescending(x => x.CreatedDate),
        TieBreaker = x => x.Id
    };

    private static ListFieldMap<TestEntity> Map(bool withSearch)
    {
        var map = new ListFieldMap<TestEntity>()
            .SortAndFilter("title", x => x.Name)
            .SortAndFilter("points", x => x.Score)
            .Filter("category", x => x.CategoryId)
            .SortAndFilter("band", x => x.Score >= 50 ? Band.High : Band.Low)
            .Sort("created", x => x.CreatedDate);
        return withSearch ? map.Search(x => x.Name) : map;
    }

    private async Task SeedAsync(params TestEntity[] entities)
    {
        _context.TestEntities.AddRange(entities);
        await _context.SaveChangesAsync();
    }

    private static TestEntity Entity(string name, int score, int day = 1, string? description = null, Guid? category = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Score = score,
        CreatedDate = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
        Description = description,
        CategoryId = category
    };

    private Task<PaginatedData<string>> ListAsync(FilterParams filters, ListQueryOptions<TestEntity>? options)
        => _context.TestEntities.AsNoTracking().ToPagedListAsync(filters, x => x.Name, options);

    #region Filters

    [Fact]
    public async Task ApplyListFilters_MappedName_FiltersOnSelectorIgnoringCase()
    {
        await SeedAsync(Entity("Alpha", 10), Entity("Beta", 20));

        var page = await ListAsync(new FilterParams { Filters = new() { ["TITLE"] = "alpha" } }, Options());

        Assert.Equal(["Alpha"], page.Items);
    }

    [Fact]
    public async Task ApplyListFilters_NameNotInMap_IsIgnored()
    {
        await SeedAsync(Entity("Alpha", 10), Entity("Beta", 20));

        // "Name" is the entity property, but the map only allows "title"
        var filters = new FilterParams
        {
            Filters = new() { ["Name"] = "Alpha" },
            ContainsFilters = new() { ["Description"] = "x" },
            MinFilters = new() { ["Score"] = "15" },
            MultiValueFilters = new() { ["Name"] = ["Alpha"] }
        };
        var page = await ListAsync(filters, Options());

        Assert.Equal(2, page.TotalRecords);
    }

    [Fact]
    public async Task ApplyListFilters_SortOnlyField_IsNotFilterable()
    {
        await SeedAsync(Entity("Alpha", 10, day: 1), Entity("Beta", 20, day: 2));

        var page = await ListAsync(new FilterParams { Filters = new() { ["created"] = "2026-01-01" } }, Options());

        Assert.Equal(2, page.TotalRecords);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("High")]
    [InlineData("high")]
    public async Task ApplyListFilters_ComputedEnum_AcceptsNumberOrName(string value)
    {
        await SeedAsync(Entity("Low", 10), Entity("High", 80));

        var page = await ListAsync(new FilterParams { Filters = new() { ["band"] = value } }, Options());

        Assert.Equal(["High"], page.Items);
    }

    [Fact]
    public async Task ApplyListFilters_UnparsableValue_IsSkipped()
    {
        await SeedAsync(Entity("Alpha", 10), Entity("Beta", 20));

        var page = await ListAsync(new FilterParams { Filters = new() { ["points"] = "many" } }, Options());

        Assert.Equal(2, page.TotalRecords);
    }

    [Fact]
    public async Task ApplyListFilters_ContainsStartsEndsWith_UseSelector()
    {
        await SeedAsync(Entity("Alphabet", 10), Entity("Alpine", 20), Entity("Beta", 30));

        var contains = await ListAsync(new FilterParams { ContainsFilters = new() { ["title"] = "PHA" } }, Options());
        var startsWith = await ListAsync(new FilterParams { StartsWithFilters = new() { ["title"] = "alp" } }, Options());
        var endsWith = await ListAsync(new FilterParams { EndsWithFilters = new() { ["title"] = "INE" } }, Options());

        Assert.Equal(["Alphabet"], contains.Items);
        Assert.Equal(2, startsWith.TotalRecords);
        Assert.Equal(["Alpine"], endsWith.Items);
    }

    [Fact]
    public async Task ApplyListFilters_Range_CombinesMinAndMax()
    {
        await SeedAsync(Entity("A", 10), Entity("B", 20), Entity("C", 30));

        var filters = new FilterParams
        {
            MinFilters = new() { ["points"] = "15" },
            MaxFilters = new() { ["points"] = "25" }
        };
        var page = await ListAsync(filters, Options());

        Assert.Equal(["B"], page.Items);
    }

    [Fact]
    public async Task ApplyListFilters_MultiValue_OnComputedEnumAndNullableGuid()
    {
        var category = Guid.NewGuid();
        await SeedAsync(Entity("Low", 10, category: category), Entity("High", 80), Entity("Other", 20));

        var byBand = await ListAsync(new FilterParams { MultiValueFilters = new() { ["band"] = ["Low", "0"] } }, Options());
        var byCategory = await ListAsync(
            new FilterParams { MultiValueFilters = new() { ["category"] = [category.ToString()] } }, Options());

        Assert.Equal(2, byBand.TotalRecords);
        Assert.Equal(["Low"], byCategory.Items);
    }

    #endregion

    #region Search

    [Fact]
    public async Task ApplyListFilters_Search_MatchesOnlyTheSearchFields()
    {
        await SeedAsync(Entity("Alpha", 10, description: "zeta"), Entity("Zeta", 20));

        var page = await ListAsync(new FilterParams { SearchTerm = "ZET" }, Options());

        // "Alpha" has "zeta" in Description, which is not a search field
        Assert.Equal(["Zeta"], page.Items);
    }

    [Fact]
    public async Task ApplyListFilters_SearchWithoutSearchFields_IsIgnored()
    {
        await SeedAsync(Entity("Alpha", 10), Entity("Beta", 20));

        var page = await ListAsync(new FilterParams { SearchTerm = "nothing" }, Options(withSearch: false));

        Assert.Equal(2, page.TotalRecords);
    }

    [Fact]
    public async Task ApplyListFilters_SeveralSearchFields_AreCombinedWithOr()
    {
        await SeedAsync(Entity("Alpha", 10, description: "here"), Entity("Beta", 20, description: "other"), Entity("Gamma", 30));
        var options = new ListQueryOptions<TestEntity>
        {
            Fields = new ListFieldMap<TestEntity>().Search(x => x.Name, x => x.Description),
            TieBreaker = x => x.Id
        };

        var page = await ListAsync(new FilterParams { SearchTerm = "e" }, options);

        // "e" is in Alpha (description) and Beta (name), not in Gamma
        Assert.Equal(2, page.TotalRecords);
        Assert.DoesNotContain("Gamma", page.Items);
    }

    #endregion

    #region Sorting

    [Fact]
    public async Task ApplyListOrder_SeveralFieldsAndDesc_AppliesInOrder()
    {
        await SeedAsync(Entity("B", 80), Entity("A", 10), Entity("C", 90), Entity("D", 20));

        var page = await ListAsync(new FilterParams { SortBy = "band desc, Title" }, Options());

        Assert.Equal(["B", "C", "A", "D"], page.Items);
    }

    [Fact]
    public async Task ApplyListOrder_NoUsableField_UsesDefaultOrder()
    {
        await SeedAsync(Entity("Old", 10, day: 1), Entity("New", 20, day: 3), Entity("Mid", 30, day: 2));

        var empty = await ListAsync(new FilterParams(), Options());
        var unknown = await ListAsync(new FilterParams { SortBy = "Name desc, nothing" }, Options());

        Assert.Equal(["New", "Mid", "Old"], empty.Items);
        Assert.Equal(["New", "Mid", "Old"], unknown.Items);
    }

    [Fact]
    public async Task ApplyListOrder_EqualValues_AreOrderedByTieBreaker()
    {
        var entities = Enumerable.Range(0, 6).Select(i => Entity($"E{i}", 10)).ToArray();
        await SeedAsync(entities);
        var expected = entities.OrderBy(e => e.Id).Select(e => e.Name).ToList();

        var first = await ListAsync(new FilterParams { SortBy = "points", PageSize = 3 }, Options());
        var second = await ListAsync(new FilterParams { SortBy = "points", PageSize = 3, PageNumber = 2 }, Options());

        Assert.Equal(expected, first.Items.Concat(second.Items));
    }

    #endregion

    #region Without a map

    [Fact]
    public async Task ToPagedListAsync_WithoutMap_UsesEntityPropertyPaths()
    {
        await SeedAsync(Entity("Alpha", 10, description: "x"), Entity("Beta", 30, description: "needle"), Entity("Gamma", 20));

        var filtered = await ListAsync(new FilterParams { Filters = new() { ["Name"] = "beta" } }, options: null);
        var sorted = await ListAsync(new FilterParams { SortBy = "Score desc" }, options: null);
        var searched = await ListAsync(new FilterParams { SearchTerm = "NEED" }, options: null);

        Assert.Equal(["Beta"], filtered.Items);
        Assert.Equal(["Beta", "Gamma", "Alpha"], sorted.Items);
        Assert.Equal(["Beta"], searched.Items);
    }

    [Fact]
    public void ApplyListOrder_WithoutMapAndUnknownColumn_LeavesQueryUnordered()
    {
        var query = _context.TestEntities.AsNoTracking();

        var result = query.ApplyListOrder("nothing");

        Assert.Same(query.Expression, result.Expression);
    }

    #endregion

    #region Paging and projection

    [Fact]
    public async Task ToPagedListAsync_CountsBeforePagingAndProjectsRows()
    {
        await SeedAsync(Enumerable.Range(1, 5).Select(i => Entity($"E{i}", i * 10, day: i)).ToArray());

        var page = await _context.TestEntities.AsNoTracking().ToPagedListAsync(
            new FilterParams { SortBy = "points", PageNumber = 2, PageSize = 2, MinFilters = new() { ["points"] = "20" } },
            x => new { x.Name, Double = x.Score * 2 },
            Options());

        Assert.Equal(4, page.TotalRecords);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(["E4", "E5"], page.Items.Select(i => i.Name));
        Assert.Equal([80, 100], page.Items.Select(i => i.Double));
    }

    #endregion
}
