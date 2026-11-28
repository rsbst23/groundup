using System.Linq.Expressions;
using GroundUp.Repositories;
using GroundUp.Tests.Unit.Repositories.TestHelpers;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for the selector overloads of <see cref="ExpressionHelper"/> (a lambda instead of a
/// property path), including a computed selector.
/// </summary>
public sealed class ExpressionHelperSelectorTests
{
    private static readonly TestEntity Low = new() { Id = Guid.NewGuid(), Name = "Low", Score = 10, Description = null };
    private static readonly TestEntity High = new() { Id = Guid.NewGuid(), Name = "High", Score = 90, Description = "Top" };
    private static readonly TestEntity[] All = [Low, High];

    private static readonly Expression<Func<TestEntity, DayOfWeek>> Computed = x => x.Score >= 50 ? DayOfWeek.Friday : DayOfWeek.Monday;

    private static List<string> Names(Expression<Func<TestEntity, bool>> predicate)
        => All.AsQueryable().Where(predicate).Select(x => x.Name).ToList();

    [Theory]
    [InlineData("5")]
    [InlineData("Friday")]
    [InlineData("friday")]
    public void BuildPredicate_ComputedEnumSelector_MatchesNumberOrName(string value)
    {
        Assert.Equal(new[] { "High" }, Names(ExpressionHelper.BuildPredicate<TestEntity>(Computed, value)));
    }

    [Fact]
    public void BuildPredicate_UnparsableValue_IsAlwaysTrue()
    {
        Assert.Equal(2, Names(ExpressionHelper.BuildPredicate<TestEntity>(Computed, "someday")).Count);
    }

    [Fact]
    public void BuildContainsPredicate_NullableStringSelector_NullNeverMatches()
    {
        Expression<Func<TestEntity, string?>> description = x => x.Description;

        Assert.Equal(new[] { "High" }, Names(ExpressionHelper.BuildContainsPredicate<TestEntity>(description, "o")));
    }

    [Fact]
    public void BuildContainsPredicate_NonStringSelector_IsAlwaysTrue()
    {
        Assert.Equal(2, Names(ExpressionHelper.BuildContainsPredicate<TestEntity>(Computed, "x")).Count);
    }

    [Fact]
    public void BuildRangePredicate_Selector_AppliesBounds()
    {
        Expression<Func<TestEntity, int>> score = x => x.Score * 2;

        Assert.Equal(new[] { "High" }, Names(ExpressionHelper.BuildRangePredicate<TestEntity>(score, "100", null)));
    }

    [Fact]
    public void BuildMultiValuePredicate_Selector_MatchesAnyValue()
    {
        Expression<Func<TestEntity, string>> name = x => x.Name;

        Assert.Equal(2, Names(ExpressionHelper.BuildMultiValuePredicate<TestEntity>(name, ["LOW", "high"])).Count);
        Assert.Equal(new[] { "Low" }, Names(ExpressionHelper.BuildMultiValuePredicate<TestEntity>(Computed, ["Monday"])));
    }

    [Fact]
    public void ApplyOrder_ThenBy_AddsSecondKey()
    {
        var rows = new[]
        {
            new TestEntity { Name = "B", Score = 1 },
            new TestEntity { Name = "A", Score = 1 },
            new TestEntity { Name = "C", Score = 0 }
        }.AsQueryable();
        Expression<Func<TestEntity, int>> score = x => x.Score;
        Expression<Func<TestEntity, string>> name = x => x.Name;

        var ordered = ExpressionHelper.ApplyOrder(rows, score, descending: true, thenBy: false);
        ordered = ExpressionHelper.ApplyOrder(ordered, name, descending: false, thenBy: true);

        Assert.Equal(new[] { "A", "B", "C" }, ordered.Select(x => x.Name));
    }

    [Fact]
    public void BuildPredicate_SelectorForAnotherType_Throws()
    {
        Expression<Func<string, int>> wrong = s => s.Length;

        Assert.Throws<ArgumentException>(() => ExpressionHelper.BuildPredicate<TestEntity>(wrong, "1"));
    }
}
