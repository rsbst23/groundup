using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents an organizational tenant. Tenants support hierarchy via
/// <see cref="ParentTenantId"/> and own roles, user memberships, and configuration.
/// </summary>
public sealed class Tenant : BaseEntity, IAuditable, ISoftDeletable
{
    /// <summary>
    /// The display name of the tenant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly unique identifier for the tenant.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// The type of tenant (Standard or Enterprise).
    /// </summary>
    public TenantType TenantType { get; set; }

    /// <summary>
    /// How users can join this tenant.
    /// </summary>
    public OnboardingMode OnboardingMode { get; set; }

    /// <summary>
    /// Optional parent tenant identifier for hierarchical tenancy.
    /// </summary>
    public Guid? ParentTenantId { get; set; }

    /// <summary>
    /// Navigation property to the parent tenant, if any.
    /// </summary>
    public Tenant? Parent { get; set; }

    /// <summary>
    /// Child tenants in the hierarchy.
    /// </summary>
    public ICollection<Tenant> Children { get; set; } = new List<Tenant>();

    /// <summary>
    /// Optional identity provider realm name for SSO federation.
    /// </summary>
    public string? RealmName { get; set; }

    /// <summary>
    /// Optional custom domain for tenant-specific access.
    /// </summary>
    public string? CustomDomain { get; set; }

    /// <summary>
    /// Whether the tenant is active. Defaults to true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The user memberships for this tenant.
    /// </summary>
    public ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();

    /// <summary>
    /// The roles owned by this tenant.
    /// </summary>
    public ICollection<Role> Roles { get; set; } = new List<Role>();

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAt { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }
}
