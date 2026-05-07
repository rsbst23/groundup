namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Categorizes roles by their origin and mutability.
/// </summary>
public enum RoleType
{
    /// <summary>Framework-defined, immutable role.</summary>
    System = 0,

    /// <summary>Application-defined role, managed by developers.</summary>
    Application = 1,

    /// <summary>User-created role within a tenant workspace.</summary>
    Workspace = 2
}
