using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Junction entity assigning a <see cref="Role"/> to a <see cref="User"/>
/// within a specific <see cref="Tenant"/> context.
/// </summary>
public sealed class UserRole : BaseEntity, IAuditable, ITenantEntity
{
    /// <summary>
    /// The user identifier (foreign key).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property to the associated user.
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// The role identifier (foreign key).
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Navigation property to the associated role.
    /// </summary>
    public Role Role { get; set; } = null!;

    /// <summary>
    /// The tenant identifier scoping this role assignment (foreign key).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the tenant scoping this assignment.
    /// </summary>
    public Tenant Tenant { get; set; } = null!;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
