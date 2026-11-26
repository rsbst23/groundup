using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Junction entity linking a <see cref="User"/> to a <see cref="Tenant"/>,
/// carrying the per-tenant external user identifier from the identity provider.
/// </summary>
public sealed class UserTenant : BaseEntity, IAuditable, ITenantEntity
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
    /// The tenant identifier (foreign key).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Navigation property to the associated tenant.
    /// </summary>
    public Tenant Tenant { get; set; } = null!;

    /// <summary>
    /// The identity provider user identifier specific to this tenant context.
    /// Null while the membership belongs to a pending user who has not signed in yet.
    /// </summary>
    public string? ExternalUserId { get; set; }

    /// <summary>
    /// The membership state. Defaults to <see cref="MembershipStatus.Active"/>.
    /// </summary>
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
