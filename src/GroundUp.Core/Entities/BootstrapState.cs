namespace GroundUp.Core.Entities;

/// <summary>
/// Singleton entity tracking whether first-run setup has been completed.
/// Has exactly two states: incomplete (the framework is in setup mode) or
/// complete (normal operation). The singleton invariant is enforced at the
/// database level via a CHECK constraint requiring <see cref="BaseEntity.Id"/>
/// equal <see cref="SentinelId"/>.
/// </summary>
public sealed class BootstrapState : BaseEntity, IAuditable
{
    /// <summary>
    /// The fixed sentinel ID for the singleton BootstrapState row.
    /// </summary>
    public static readonly Guid SentinelId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Whether first-run setup has been completed.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// The UTC timestamp when setup was completed. Null while setup is incomplete.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The ID of the user who completed setup. Null while setup is incomplete.
    /// </summary>
    public Guid? CompletedBy { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTime? UpdatedAt { get; set; }

    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
