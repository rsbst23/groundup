namespace GroundUp.Core.Exceptions;

/// <summary>
/// Base exception for GroundUp infrastructure and cross-cutting errors.
/// Maps to HTTP 500 by default in ExceptionHandlingMiddleware.
/// Business logic should use <see cref="Results.OperationResult{T}"/> instead of throwing exceptions.
/// </summary>
public class GroundUpException : Exception
{
    /// <summary>
    /// Creates a new <see cref="GroundUpException"/> with the specified message.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    public GroundUpException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="GroundUpException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public GroundUpException(string message, Exception innerException) : base(message, innerException) { }
}
