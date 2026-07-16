using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Services;

/// <summary>
/// Request parameters for initiating an authentication flow.
/// </summary>
/// <param name="FlowType">The type of authentication flow to initiate.</param>
/// <param name="OrganizationName">
/// The organization name for <see cref="Auth.Core.Enums.FlowType.NewOrganization"/> flows.
/// Stored on the AuthFlowState for use during callback processing.
/// </param>
/// <param name="ReturnUrl">
/// Optional post-authentication redirect URL for the client.
/// </param>
public sealed record FlowInitiationRequest(
    FlowType FlowType,
    string? OrganizationName = null,
    string? ReturnUrl = null);
