namespace GroundUp.Core.Results;

/// <summary>
/// The single standardized result type for all service and repository returns.
/// Use static factory methods to create instances. Business logic returns
/// <see cref="Fail"/> instead of throwing exceptions.
/// </summary>
/// <typeparam name="T">The type of data returned on success.</typeparam>
public sealed class OperationResult<T>
{
    /// <summary>
    /// The data payload. Populated on success, null on failure.
    /// </summary>
    public T? Data { get; init; }

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
    /// Creates a successful result with the specified data.
    /// </summary>
    /// <param name="data">The result data.</param>
    /// <param name="message">An optional success message.</param>
    /// <param name="statusCode">The HTTP status code. Defaults to 200.</param>
    /// <returns>A successful <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> Ok(T? data, string message = "Success", int statusCode = 200) =>
        new() { Data = data, Success = true, Message = message, StatusCode = statusCode };

    /// <summary>
    /// Creates a failure result with the specified details.
    /// </summary>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="statusCode">The HTTP status code for this failure.</param>
    /// <param name="errorCode">An optional machine-readable error code.</param>
    /// <param name="errors">An optional list of detailed error messages.</param>
    /// <returns>A failed <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> Fail(string message, int statusCode, string? errorCode = null, List<string>? errors = null) =>
        new() { Success = false, Message = message, StatusCode = statusCode, ErrorCode = errorCode, Errors = errors };

    /// <summary>
    /// Creates a 404 Not Found failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 404 <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> NotFound(string message = "Item not found") =>
        Fail(message, 404, Core.ErrorCodes.NotFound);

    /// <summary>
    /// Creates a 400 Bad Request failure result.
    /// </summary>
    /// <param name="message">A human-readable error message.</param>
    /// <param name="errors">An optional list of validation error details.</param>
    /// <returns>A 400 <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> BadRequest(string message, List<string>? errors = null) =>
        Fail(message, 400, Core.ErrorCodes.ValidationFailed, errors);

    /// <summary>
    /// Creates a 401 Unauthorized failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 401 <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> Unauthorized(string message = "Unauthorized") =>
        Fail(message, 401, Core.ErrorCodes.Unauthorized);

    /// <summary>
    /// Creates a 403 Forbidden failure result.
    /// </summary>
    /// <param name="message">An optional error message.</param>
    /// <returns>A 403 <see cref="OperationResult{T}"/>.</returns>
    public static OperationResult<T> Forbidden(string message = "Forbidden") =>
        Fail(message, 403, Core.ErrorCodes.Forbidden);
}
