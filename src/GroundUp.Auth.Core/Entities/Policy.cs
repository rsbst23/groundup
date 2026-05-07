using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents a policy that groups related permissions. Policies are
/// assigned to roles via <see cref="RolePolicy"/> and are tenant-scoped.
/// </summary>
public sealed class Policy : BaseEntity, IAuditable, ITenantEntity
{
    /// <summary>
    /// The display name of the policy.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the policy's purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The tenant that owns this policy (foreign key).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the owning tenant.
    /// </summary>
    public Tenant Tenant { get; set; } = null!;

    /// <summary>
    /// The role assignments that include this policy.
    /// </summary>
    public ICollection<RolePolicy> RolePolicies { get; set; } = new List<RolePolicy>();

    /// <summary>
    /// The permissions included in this policy.
    /// </summary>
    public ICollection<PolicyPermission> PolicyPermissions { get; set; } = new List<PolicyPermission>();

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
