using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents a role within a tenant. Roles group policies and are assigned
/// to users via <see cref="UserRole"/>. Roles are tenant-scoped.
/// </summary>
public sealed class Role : BaseEntity, IAuditable, ITenantEntity
{
    /// <summary>
    /// The display name of the role.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the role's purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The category of this role (System, Application, or Workspace).
    /// </summary>
    public RoleType RoleType { get; set; }

    /// <summary>
    /// The tenant that owns this role (foreign key).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the owning tenant.
    /// </summary>
    public Tenant Tenant { get; set; } = null!;

    /// <summary>
    /// Whether this is a system-defined immutable role.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// The policies assigned to this role.
    /// </summary>
    public ICollection<RolePolicy> RolePolicies { get; set; } = new List<RolePolicy>();

    /// <summary>
    /// The user assignments for this role.
    /// </summary>
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
