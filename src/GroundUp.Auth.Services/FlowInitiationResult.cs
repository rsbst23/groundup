namespace GroundUp.Auth.Services;

/// <summary>
/// Result of a successful flow initiation, containing the redirect URL
/// to send the user to the identity provider and the persisted flow state ID.
/// </summary>
/// <param name="RedirectUrl">The fully constructed authorization URL to redirect the user to.</param>
/// <param name="FlowStateId">The ID of the persisted AuthFlowState row for correlation at callback.</param>
public sealed record FlowInitiationResult(
    string RedirectUrl,
    Guid FlowStateId);
