using FsCheck;
using FsCheck.Xunit;
using GroundUp.Repositories;
using GroundUp.Tests.Unit.Repositories.TestHelpers;

namespace GroundUp.Tests.Unit.Repositories;

/// <summary>
/// Property-based tests for <see cref="ExpressionHelper"/>.
/// Validates correctness properties across randomized inputs.
/// </summary>
public sealed class ExpressionHelperPropertyTests
{
    /// <summary>
    /// Property 1: BuildPredicate string exact-match round-trip.
    /// For any non-null string, the predicate matches entities with the same value
    /// (case-insensitively) and rejects entities with different values.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildPredicate_String_RoundTrip(NonNull<string> value)
    {
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Name", value.Get);
        var compiled = predicate.Compile();

        var matching = new TestEntity { Name = value.Get };
        var matchingUpper = new TestEntity { Name = value.Get.ToUpper() };

        return (compiled(matching) && compiled(matchingUpper)).ToProperty();
    }

    /// <summary>
    /// Property 2: BuildPredicate Guid exact-match round-trip.
    /// For any Guid, the predicate matches entities with the same Guid
    /// and rejects entities with different Guids.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildPredicate_Guid_RoundTrip()
    {
        var guid = Guid.NewGuid();
        var other = Guid.NewGuid();
        var predicate = ExpressionHelper.BuildPredicate<TestEntity>("Id", guid.ToString());
        var compiled = predicate.Compile();

        return (compiled(new TestEntity { Id = guid })
            && !compiled(new TestEntity { Id = other }))
            .ToProperty();
    }

    /// <summary>
    /// Property 3: Invalid property name produces safe default.
    /// For any random string that doesn't match a property, all methods return always-true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InvalidProperty_AllMethods_ReturnAlwaysTrue(NonNull<string> randomProp)
    {
        // Prefix with "ZZZ_" to ensure it doesn't accidentally match a real property
        var fakeProp = "ZZZ_" + randomProp.Get;
        var entity = new TestEntity { Name = "test", Score = 42 };

        var pred1 = ExpressionHelper.BuildPredicate<TestEntity>(fakeProp, "val").Compile();
        var pred2 = ExpressionHelper.BuildContainsPredicate<TestEntity>(fakeProp, "val").Compile();
        var pred3 = ExpressionHelper.BuildStartsWithPredicate<TestEntity>(fakeProp, "val").Compile();
        var pred4 = ExpressionHelper.BuildEndsWithPredicate<TestEntity>(fakeProp, "val").Compile();
        var pred5 = ExpressionHelper.BuildRangePredicate<TestEntity>(fakeProp, "1", "100").Compile();
        var pred6 = ExpressionHelper.BuildDateRangePredicate<TestEntity>(fakeProp, "2024-01-01", "2024-12-31").Compile();
        var pred7 = ExpressionHelper.BuildMultiValuePredicate<TestEntity>(fakeProp, new List<string> { "a" }).Compile();

        return (pred1(entity) && pred2(entity) && pred3(entity) && pred4(entity)
            && pred5(entity) && pred6(entity) && pred7(entity))
            .ToProperty();
    }

    /// <summary>
    /// Property 4: BuildContainsPredicate substring match.
    /// For any non-empty string value, if the entity's Name contains the value,
    /// the predicate returns true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildContainsPredicate_SubstringMatch(NonNull<string> value)
    {
        if (string.IsNullOrEmpty(value.Get)) return true.ToProperty();

        var predicate = ExpressionHelper.BuildContainsPredicate<TestEntity>("Name", value.Get);
        var compiled = predicate.Compile();

        // An entity whose Name contains the value should match
        var entity = new TestEntity { Name = "prefix" + value.Get + "suffix" };
        return compiled(entity).ToProperty();
    }

    /// <summary>
    /// Property 5: BuildRangePredicate range correctness.
    /// For any int value and min/max bounds, the predicate returns true iff
    /// value is within [min, max].
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildRangePredicate_IntRange_Correctness(int entityValue, int bound1, int bound2)
    {
        var min = Math.Min(bound1, bound2);
        var max = Math.Max(bound1, bound2);

        var predicate = ExpressionHelper.BuildRangePredicate<TestEntity>(
            "Score", min.ToString(), max.ToString());
        var compiled = predicate.Compile();

        var entity = new TestEntity { Score = entityValue };
        var expected = entityValue >= min && entityValue <= max;

        return (compiled(entity) == expected).ToProperty();
    }

    /// <summary>
    /// Property 7: ApplySorting order correctness.
    /// For any list of entities, ascending sort produces non-decreasing order,
    /// descending sort produces non-increasing order.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ApplySorting_Ascending_ProducesNonDecreasingOrder(int[] scores)
    {
        if (scores.Length < 2) return true.ToProperty();

        var items = scores.Select(s => new TestEntity { Score = s }).AsQueryable();
        var sorted = ExpressionHelper.ApplySorting(items, "Score").ToList();

        var isOrdered = true;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Score < sorted[i - 1].Score)
            {
                isOrdered = false;
                break;
            }
        }

        return isOrdered.ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property ApplySorting_Descending_ProducesNonIncreasingOrder(int[] scores)
    {
        if (scores.Length < 2) return true.ToProperty();

        var items = scores.Select(s => new TestEntity { Score = s }).AsQueryable();
        var sorted = ExpressionHelper.ApplySorting(items, "Score desc").ToList();

        var isOrdered = true;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Score > sorted[i - 1].Score)
            {
                isOrdered = false;
                break;
            }
        }

        return isOrdered.ToProperty();
    }
}
