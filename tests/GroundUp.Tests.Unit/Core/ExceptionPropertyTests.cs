using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Exceptions;

namespace GroundUp.Tests.Unit.Core;

/// <summary>
/// Property-based tests for the exception hierarchy.
/// Validates correctness properties from the Phase 1 design document.
/// </summary>
public sealed class ExceptionPropertyTests
{
    /// <summary>
    /// Property 4: Exception constructors preserve message.
    /// For any non-null string, GroundUpException(message).Message
    /// and NotFoundException(message).Message return the same string.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GroundUpException_PreservesMessage(NonNull<string> message)
    {
        var ex = new GroundUpException(message.Get);
        return (ex.Message == message.Get).ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property NotFoundException_PreservesMessage(NonNull<string> message)
    {
        var ex = new NotFoundException(message.Get);
        return (ex.Message == message.Get).ToProperty();
    }

    [Property(MaxTest = 100)]
    public Property GroundUpException_WithInnerException_PreservesMessageAndInner(NonNull<string> message)
    {
        var inner = new InvalidOperationException("inner");
        var ex = new GroundUpException(message.Get, inner);
        return (ex.Message == message.Get && ex.InnerException == inner).ToProperty();
    }
}
