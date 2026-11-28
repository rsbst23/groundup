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
    /// The caller lacks permissions a <c>[RequiresPermission]</c> method needs. <c>errors</c> holds the
    /// permission keys the caller does not have (all of them are required).
    /// </summary>
    public const string PermissionRequired = "PERMISSION_REQUIRED";

    /// <summary>
    /// The caller has none of the system roles a <c>[RequiresRole]</c> method needs. <c>errors</c> holds
    /// the listed roles (any one of them is enough).
    /// </summary>
    public const string RoleRequired = "ROLE_REQUIRED";

    /// <summary>
    /// The operation needs a current tenant and the caller has none (for example a tenantless session).
    /// </summary>
    public const string TenantRequired = "TENANT_REQUIRED";

    /// <summary>
    /// The session has reached its absolute lifetime. The user must sign in again with credentials
    /// (the client starts a login with <c>forceLogin=true</c>).
    /// </summary>
    public const string ReauthRequired = "REAUTH_REQUIRED";

    /// <summary>
    /// The caller is authenticated by the identity provider but has no GroundUp user account.
    /// </summary>
    public const string UserNotFound = "USER_NOT_FOUND";

    /// <summary>
    /// The request conflicts with the current state of the resource.
    /// </summary>
    public const string Conflict = "CONFLICT";

    /// <summary>
    /// An unexpected internal error occurred.
    /// </summary>
    public const string InternalError = "INTERNAL_ERROR";
}
