namespace GroundUp.Core.Exceptions;

/// <summary>
/// Thrown when an entity is not found by ID.
/// Maps to HTTP 404 in ExceptionHandlingMiddleware.
/// </summary>
public sealed class NotFoundException : GroundUpException
{
    /// <summary>
    /// Creates a new <see cref="NotFoundException"/> with the specified message.
    /// </summary>
    /// <param name="message">A human-readable description, typically including the entity type and ID.</param>
    public NotFoundException(string message) : base(message) { }
}
