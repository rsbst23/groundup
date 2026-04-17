using GroundUp.Repositories;
using GroundUp.Tests.Unit.Repositories.TestHelpers;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Example-based unit tests for <see cref="ExpressionHelper"/>.
/// Covers all predicate methods, sorting, and edge cases.
/// </summary>
public sealed class ExpressionHelperTests
{
    #region BuildPredicate — String

    [Fact]
    public void BuildPredicate_StringProperty_CaseInsensitiveMatch()
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Name", "Alice");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.True(compiled(new TestEntity { Name = "alice" }));
        Assert.True(compiled(new TestEntity { Name = "ALICE" }));
        Assert.False(compiled(new TestEntity { Name = "Bob" }));
    }

    [Fact]
    public void BuildPredicate_StringProperty_NullValue_ReturnsFalse()
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Name", "test");
        var compiled = predicate.Compile();

        Assert.False(compiled(new TestEntity { Name = null! }));
    }

    #endregion

    #region BuildPredicate — Guid

    [Fact]
    public void BuildPredicate_GuidProperty_ExactMatch()
    {
        var guid = Guid.NewGuid();
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Id", guid.ToString());
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Id = guid }));
        Assert.False(compiled(new TestEntity { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void BuildPredicate_NullableGuidProperty_ExactMatch()
    {
        var guid = Guid.NewGuid();
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("CategoryId", guid.ToString());
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { CategoryId = guid }));
        Assert.False(compiled(new TestEntity { CategoryId = null }));
        Assert.False(compiled(new TestEntity { CategoryId = Guid.NewGuid() }));
    }

    #endregion

    #region BuildPredicate — Int

    [Fact]
    public void BuildPredicate_IntProperty_ExactMatch()
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Score", "42");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 42 }));
        Assert.False(compiled(new TestEntity { Score = 99 }));
    }

    #endregion

    #region BuildPredicate — DateTime

    [Fact]
    public void BuildPredicate_DateTimeProperty_ExactMatch()
    {
        var date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("CreatedDate", "2024-06-15");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { CreatedDate = date }));
        Assert.False(compiled(new TestEntity { CreatedDate = date.AddDays(1) }));
    }

    #endregion

    #region BuildPredicate — Invalid Property

    [Fact]
    public void BuildPredicate_InvalidProperty_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("NonExistent", "value");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "anything" }));
    }

    [Fact]
    public void BuildPredicate_UnparseableValue_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Score", "not-a-number");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 42 }));
    }

    #endregion

    #region BuildContainsPredicate

    [Fact]
    public void BuildContainsPredicate_SubstringMatch_CaseInsensitive()
    {
        var predicate = ExpressionHelper.BuildContainsPredicate<TestEntity>("Name", "lic");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.True(compiled(new TestEntity { Name = "ALICE" }));
        Assert.False(compiled(new TestEntity { Name = "Bob" }));
    }

    [Fact]
    public void BuildContainsPredicate_NullProperty_ReturnsFalse()
    {
        var predicate = ExpressionHelper.BuildContainsPredicate<TestEntity>("Description", "test");
        var compiled = predicate.Compile();

        Assert.False(compiled(new TestEntity { Description = null }));
    }

    [Fact]
    public void BuildContainsPredicate_NonStringProperty_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildContainsPredicate<TestEntity>("Score", "42");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 99 }));
    }

    #endregion

    #region BuildStartsWithPredicate

    [Fact]
    public void BuildStartsWithPredicate_PrefixMatch_CaseInsensitive()
    {
        var predicate = ExpressionHelper.BuildStartsWithPredicate<TestEntity>("Name", "al");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.True(compiled(new TestEntity { Name = "ALBERT" }));
        Assert.False(compiled(new TestEntity { Name = "Bob" }));
    }

    #endregion

    #region BuildEndsWithPredicate

    [Fact]
    public void BuildEndsWithPredicate_SuffixMatch_CaseInsensitive()
    {
        var predicate = ExpressionHelper.BuildEndsWithPredicate<TestEntity>("Name", "ce");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.True(compiled(new TestEntity { Name = "GRACE" }));
        Assert.False(compiled(new TestEntity { Name = "Bob" }));
    }

    #endregion

    #region BuildMultiValuePredicate

    [Fact]
    public void BuildMultiValuePredicate_StringProperty_CaseInsensitiveInClause()
    {
        var predicate = ExpressionHelper.BuildMultiValuePredicate<TestEntity>("Name", new List<string> { "Alice", "Bob" });
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.True(compiled(new TestEntity { Name = "bob" }));
        Assert.False(compiled(new TestEntity { Name = "Charlie" }));
    }

    [Fact]
    public void BuildMultiValuePredicate_IntProperty_InClause()
    {
        var predicate = ExpressionHelper.BuildMultiValuePredicate<TestEntity>("Score", new List<string> { "10", "20", "30" });
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 10 }));
        Assert.True(compiled(new TestEntity { Score = 30 }));
        Assert.False(compiled(new TestEntity { Score = 15 }));
    }

    [Fact]
    public void BuildMultiValuePredicate_EmptyList_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildMultiValuePredicate<TestEntity>("Name", new List<string>());
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "anything" }));
    }

    #endregion

    #region BuildRangePredicate

    [Fact]
    public void BuildRangePredicate_MinOnly_GreaterThanOrEqual()
    {
        var predicate = ExpressionHelper.BuildRangePredicate<TestEntity>("Score", "10", null);
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 10 }));
        Assert.True(compiled(new TestEntity { Score = 50 }));
        Assert.False(compiled(new TestEntity { Score = 5 }));
    }

    [Fact]
    public void BuildRangePredicate_MaxOnly_LessThanOrEqual()
    {
        var predicate = ExpressionHelper.BuildRangePredicate<TestEntity>("Score", null, "50");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 50 }));
        Assert.True(compiled(new TestEntity { Score = 10 }));
        Assert.False(compiled(new TestEntity { Score = 51 }));
    }

    [Fact]
    public void BuildRangePredicate_MinAndMax_BothInclusive()
    {
        var predicate = ExpressionHelper.BuildRangePredicate<TestEntity>("Score", "10", "50");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 10 }));
        Assert.True(compiled(new TestEntity { Score = 30 }));
        Assert.True(compiled(new TestEntity { Score = 50 }));
        Assert.False(compiled(new TestEntity { Score = 9 }));
        Assert.False(compiled(new TestEntity { Score = 51 }));
    }

    [Fact]
    public void BuildRangePredicate_StringProperty_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildRangePredicate<TestEntity>("Name", "a", "z");
        var compiled = predicate.Compile();

        // String is not in ComparableTypes, so always-true
        Assert.True(compiled(new TestEntity { Name = "anything" }));
    }

    #endregion

    #region BuildDateRangePredicate

    [Fact]
    public void BuildDateRangePredicate_ValidDateRange()
    {
        var predicate = ExpressionHelper.BuildDateRangePredicate<TestEntity>(
            "CreatedDate", "2024-01-01", "2024-12-31");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { CreatedDate = new DateTime(2024, 6, 15) }));
        Assert.False(compiled(new TestEntity { CreatedDate = new DateTime(2023, 12, 31) }));
        Assert.False(compiled(new TestEntity { CreatedDate = new DateTime(2025, 1, 1) }));
    }

    [Fact]
    public void BuildDateRangePredicate_UnparseableDate_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildDateRangePredicate<TestEntity>(
            "CreatedDate", "not-a-date", "also-not-a-date");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { CreatedDate = DateTime.UtcNow }));
    }

    [Fact]
    public void BuildDateRangePredicate_NonDateProperty_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildDateRangePredicate<TestEntity>(
            "Score", "2024-01-01", "2024-12-31");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Score = 42 }));
    }

    #endregion

    #region BuildSearchPredicate

    [Fact]
    public void BuildSearchPredicate_MatchesAnyStringProperty()
    {
        var predicate = ExpressionHelper.BuildSearchPredicate<TestEntity>("alice");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "Alice Smith", Description = "other" }));
        Assert.True(compiled(new TestEntity { Name = "Bob", Description = "alice is here" }));
        Assert.False(compiled(new TestEntity { Name = "Bob", Description = "nothing" }));
    }

    [Fact]
    public void BuildSearchPredicate_NullOrEmpty_ReturnsAlwaysTrue()
    {
        var predicate = ExpressionHelper.BuildSearchPredicate<TestEntity>(null);
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "anything" }));
    }

    #endregion

    #region ApplySorting

    [Fact]
    public void ApplySorting_Ascending()
    {
        var items = new List<TestEntity>
        {
            new() { Name = "Charlie" },
            new() { Name = "Alice" },
            new() { Name = "Bob" }
        }.AsQueryable();

        var sorted = ExpressionHelper.ApplySorting(items, "Name").ToList();

        Assert.Equal("Alice", sorted[0].Name);
        Assert.Equal("Bob", sorted[1].Name);
        Assert.Equal("Charlie", sorted[2].Name);
    }

    [Fact]
    public void ApplySorting_Descending()
    {
        var items = new List<TestEntity>
        {
            new() { Name = "Alice" },
            new() { Name = "Charlie" },
            new() { Name = "Bob" }
        }.AsQueryable();

        var sorted = ExpressionHelper.ApplySorting(items, "Name desc").ToList();

        Assert.Equal("Charlie", sorted[0].Name);
        Assert.Equal("Bob", sorted[1].Name);
        Assert.Equal("Alice", sorted[2].Name);
    }

    [Fact]
    public void ApplySorting_MultiColumn()
    {
        var items = new List<TestEntity>
        {
            new() { Score = 10, Name = "Charlie" },
            new() { Score = 10, Name = "Alice" },
            new() { Score = 20, Name = "Bob" }
        }.AsQueryable();

        var sorted = ExpressionHelper.ApplySorting(items, "Score, Name").ToList();

        Assert.Equal("Alice", sorted[0].Name);
        Assert.Equal("Charlie", sorted[1].Name);
        Assert.Equal("Bob", sorted[2].Name);
    }

    [Fact]
    public void ApplySorting_InvalidProperty_ReturnsUnchanged()
    {
        var items = new List<TestEntity>
        {
            new() { Name = "Charlie" },
            new() { Name = "Alice" }
        }.AsQueryable();

        var sorted = ExpressionHelper.ApplySorting(items, "NonExistent").ToList();

        Assert.Equal("Charlie", sorted[0].Name);
        Assert.Equal("Alice", sorted[1].Name);
    }

    [Fact]
    public void ApplySorting_NullExpression_ReturnsUnchanged()
    {
        var items = new List<TestEntity>
        {
            new() { Name = "Charlie" },
            new() { Name = "Alice" }
        }.AsQueryable();

        var sorted = ExpressionHelper.ApplySorting(items, null).ToList();

        Assert.Equal("Charlie", sorted[0].Name);
        Assert.Equal("Alice", sorted[1].Name);
    }

    #endregion

    #region Nested Property Access

    [Fact]
    public void BuildPredicate_NestedProperty_NotFound_ReturnsAlwaysTrue()
    {
        // TestEntity has no navigation properties, so "Foo.Bar" should return always-true
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Foo.Bar", "value");
        var compiled = predicate.Compile();

        Assert.True(compiled(new TestEntity { Name = "anything" }));
    }

    #endregion
}
