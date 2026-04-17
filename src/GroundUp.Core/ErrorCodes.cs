namespace GroundUp.Core;

/// <summary>
/// Standardized error code constants used in <see cref="Results.OperationResult{T}"/>
/// across all modules. Use these instead of magic strings to ensure consistency.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// The requested resource was not found.
    /// </summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>
    /// One or more validation rules failed.
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// The request lacks valid authentication credentials.
    /// </summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>
    /// The authenticated user does not have permission for this operation.
    /// </summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>
    /// The request conflicts with the current state of the resource.
    /// </summary>
    public const string Conflict = "CONFLICT";

    /// <summary>
    /// An unexpected internal error occurred.
    /// </summary>
    public const string InternalError = "INTERNAL_ERROR";
}
