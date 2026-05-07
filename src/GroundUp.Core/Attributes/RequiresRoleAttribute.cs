namespace GroundUp.Core.Attributes;

/// <summary>
/// Declares the role(s) required to invoke a service method.
/// Place on service interface methods to enable role-based enforcement
/// by the authorization decorator as an alternative to granular permission checks.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresRoleAttribute : Attribute
{
    /// <summary>
    /// The role names required to invoke the decorated method.
    /// The user must hold at least one of the listed roles (OR semantics).
    /// </summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresRoleAttribute"/> class.
    /// </summary>
    /// <param name="roles">One or more role names required to invoke the method (e.g., "Admin", "Manager").</param>
    public RequiresRoleAttribute(params string[] roles)
    {
        Roles = roles;
    }
}
