using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Represents an authenticated user in the system. Users connect to tenants
/// via <see cref="UserTenant"/> and receive role assignments via <see cref="UserRole"/>.
/// </summary>
public sealed class User : BaseEntity, IAuditable
{
    /// <summary>
    /// The external identity provider user identifier (e.g., Auth0 sub claim).
    /// </summary>
    public string ExternalUserId { get; set; } = string.Empty;

    /// <summary>
    /// The user's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The user's display name shown in the UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user account is active. Defaults to true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The tenant memberships for this user.
    /// </summary>
    public ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();

    /// <summary>
    /// The role assignments for this user.
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
