namespace GroundUp.Core.Exceptions;

/// <summary>
/// Thrown when the current user lacks the required permissions or roles to perform an operation.
/// Maps to HTTP 403 in ExceptionHandlingMiddleware.
/// </summary>
public sealed class ForbiddenAccessException : GroundUpException
{
    /// <summary>
    /// Creates a new <see cref="ForbiddenAccessException"/> with the specified message.
    /// </summary>
    /// <param name="message">A human-readable description of the access denial.</param>
    public ForbiddenAccessException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="ForbiddenAccessException"/> with a default message.
    /// </summary>
    public ForbiddenAccessException() : base("Forbidden") { }
}
