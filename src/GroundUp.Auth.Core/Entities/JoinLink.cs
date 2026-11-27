using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// A shareable link that lets anyone who has it join a tenant with a role (GU-68). Joining creates
/// an Active membership directly (no pending step). The token is never stored, only its SHA-256 hash.
/// </summary>
public sealed class JoinLink : BaseEntity, ITenantEntity, IAuditable, ISoftDeletable
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The role granted to people who join.</summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Who created (or last regenerated) the link. Each use re-checks that this user may still grant the role.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the link token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>When the link stops working (UTC); null for no expiry.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>How many people may join with it; null for no limit.</summary>
    public int? MaxUses { get; set; }

    /// <summary>How many people have joined with it.</summary>
    public int UseCount { get; set; }

    /// <summary>The stored status (Active or Revoked).</summary>
    public JoinLinkStatus Status { get; set; } = JoinLinkStatus.Active;

    /// <summary>When the link was revoked (UTC).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Who revoked the link.</summary>
    public Guid? RevokedByUserId { get; set; }

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
