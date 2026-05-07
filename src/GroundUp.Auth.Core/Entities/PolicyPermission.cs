using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Junction entity linking a <see cref="Policy"/> to a <see cref="Permission"/>.
/// </summary>
public sealed class PolicyPermission : BaseEntity, IAuditable
{
    /// <summary>
    /// The policy identifier (foreign key).
    /// </summary>
    public Guid PolicyId { get; set; }

    /// <summary>
    /// Navigation property to the associated policy.
    /// </summary>
    public Policy Policy { get; set; } = null!;

    /// <summary>
    /// The permission identifier (foreign key).
    /// </summary>
    public Guid PermissionId { get; set; }

    /// <summary>
    /// Navigation property to the associated permission.
    /// </summary>
    public Permission Permission { get; set; } = null!;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
