using GroundUp.Core.Entities;

namespace GroundUp.Auth.Core.Entities;

/// <summary>
/// Junction entity linking a <see cref="Role"/> to a <see cref="Policy"/>.
/// </summary>
public sealed class RolePolicy : BaseEntity, IAuditable
{
    /// <summary>
    /// The role identifier (foreign key).
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Navigation property to the associated role.
    /// </summary>
    public Role Role { get; set; } = null!;

    /// <summary>
    /// The policy identifier (foreign key).
    /// </summary>
    public Guid PolicyId { get; set; }

    /// <summary>
    /// Navigation property to the associated policy.
    /// </summary>
    public Policy Policy { get; set; } = null!;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
