using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// An invitation to join a tenant (GU-68). Creating one creates a Pending membership for
/// <see cref="UserId"/> with the role already granted; accepting it activates the membership.
/// The token is never stored, only its SHA-256 hash.
/// </summary>
public sealed class Invitation : BaseEntity, ITenantEntity, IAuditable, ISoftDeletable
{
    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>The invited email address, as entered.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The email trimmed and lower-cased, for matching.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>The role the invitation grants.</summary>
    public Guid RoleId { get; set; }

    /// <summary>The user (usually a pending user) whose membership the invitation created.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Who created (or last resent) the invitation. Acceptance re-checks that this user may still
    /// grant the role.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the invitation token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>When the invitation stops working (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>The stored status (never <see cref="InvitationStatus.Expired"/>).</summary>
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    /// <summary>Who accepted the invitation.</summary>
    public Guid? AcceptedByUserId { get; set; }

    /// <summary>When the invitation was accepted (UTC).</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>When the invitation was revoked (UTC).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Who revoked the invitation.</summary>
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
