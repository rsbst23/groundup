namespace GroundUp.Core.Results;

/// <summary>
/// Non-generic result type for void operations (e.g., DeleteAsync) that carry
/// success/failure status without a data payload. Mirrors the factory methods
/// of <see cref="OperationResult{T}"/> without the generic parameter.
/// </summary>
public sealed class OperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// A human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = "Success";

    /// <summary>
    /// A list of error details when the operation fails. Null on success.
    /// </summary>
    public List<string>? Errors { get; init; }

    /// <summary>
    /// The HTTP status code associated with this result.
    /// </summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>
    /// A machine-readable error code from <see cref="ErrorCodes"/>. Null on success.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Creates a successful result with an optional message.
    /// </summary>
    /// <param name="message">An optional success message.</param>
    /// <param name="statusCode">The HTTP status code. Defaults to 200.</param>
    /// <returns>A successful <see cref="OperationResult"/>.</returns>
    public static OperationResult Ok(string message = "Success", int statusCode = 200) =>
        new() { Success = true, Message = message, StatusCode = statusCode };

    /// <summary>
    /// Creates a failure result with the specified details.
    /// </summary>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="statusCode">The HTTP status code for this failure.</param>
    /// <param name="errorCode">An optional machine-readable error code.</param>
    /// <param name="errors">An optional list of detailed error messages.</param>
    /// <returns>A failed <see cref="OperationResult"/>.</returns>
    public static OperationResult Fail(string message, int statusCode, string? errorCode = null, List<string>? errors = null) =>
        new() { Success = false, Message = message, StatusCode = statusCode, ErrorCode = errorCode, Errors = errors };

    /// <summary>
    /// Creates a 404 Not Found failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 404 <see cref="OperationResult"/>.</returns>
    public static OperationResult NotFound(string message = "Item not found") =>
        Fail(message, 404, ErrorCodes.NotFound);

    /// <summary>
    /// Creates a 400 Bad Request failure result.
    /// </summary>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="errors">An optional list of validation error details.</param>
    /// <returns>A 400 <see cref="OperationResult"/>.</returns>
    public static OperationResult BadRequest(string message, List<string>? errors = null) =>
        Fail(message, 400, ErrorCodes.ValidationFailed, errors);

    /// <summary>
    /// Creates a 401 Unauthorized failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 401 <see cref="OperationResult"/>.</returns>
    public static OperationResult Unauthorized(string message = "Unauthorized") =>
        Fail(message, 401, ErrorCodes.Unauthorized);

    /// <summary>
    /// Creates a 403 Forbidden failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 403 <see cref="OperationResult"/>.</returns>
    public static OperationResult Forbidden(string message = "Forbidden") =>
        Fail(message, 403, ErrorCodes.Forbidden);
}
