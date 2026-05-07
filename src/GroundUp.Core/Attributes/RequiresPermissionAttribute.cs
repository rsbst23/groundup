namespace GroundUp.Core.Attributes;

/// <summary>
/// Declares the permission(s) required to invoke a service method.
/// Place on service interface methods to enable permission enforcement
/// by the authorization decorator without coupling the service implementation to security logic.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresPermissionAttribute : Attribute
{
    /// <summary>
    /// The permission keys required to invoke the decorated method.
    /// All listed permissions must be satisfied (AND semantics).
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresPermissionAttribute"/> class.
    /// </summary>
    /// <param name="permissions">One or more permission keys required to invoke the method (e.g., "settings.read", "users.manage").</param>
    public RequiresPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }
}
