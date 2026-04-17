using FsCheck;
using FsCheck.Xunit;
using GroundUp.Events;

namespace GroundUp.Tests.Unit.Events;

/// <summary>
/// Unit and property-based tests for <see cref="BaseEvent"/>.
/// </summary>
public sealed class BaseEventTests
{
    private record TestEvent : BaseEvent;

    [Fact]
    public void OccurredAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var evt = new TestEvent();
        var after = DateTime.UtcNow;

        Assert.True(evt.OccurredAt >= before);
        Assert.True(evt.OccurredAt <= after);
    }

    [Fact]
    public void TenantIdAndUserId_DefaultToNull()
    {
        var evt = new TestEvent();

        Assert.Null(evt.TenantId);
        Assert.Null(evt.UserId);
    }

    /// <summary>
    /// Property 1: BaseEvent EventId uniqueness.
    /// Two independently constructed events have distinct non-empty EventIds.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventId_IsNonEmptyAndUnique()
    {
        var evt1 = new TestEvent();
        var evt2 = new TestEvent();

        return (evt1.EventId != Guid.Empty
            && evt2.EventId != Guid.Empty
            && evt1.EventId != evt2.EventId)
            .ToProperty();
    }
}
