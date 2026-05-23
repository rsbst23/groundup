namespace GroundUp.Auth.Core.Enums;

/// <summary>
/// Represents the lifecycle status of an <see cref="Entities.AuthFlowState"/> row.
/// Transitions are one-way: Pending → Consumed | Failed | Expired.
/// </summary>
public enum FlowStatus
{
    /// <summary>Flow is active and awaiting callback consumption.</summary>
    Pending = 0,

    /// <summary>Flow was successfully consumed by the OAuth callback.</summary>
    Consumed = 1,

    /// <summary>Flow expired before being consumed (sweeper-driven).</summary>
    Expired = 2,

    /// <summary>Flow was explicitly marked as failed.</summary>
    Failed = 3
}
