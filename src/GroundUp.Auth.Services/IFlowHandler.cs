using GroundUp.Auth.Core.Enums;

namespace GroundUp.Auth.Services;

/// <summary>
/// Strategy interface for handling OAuth callback flows.
/// Each implementation processes a single <see cref="FlowType"/> and encapsulates
/// the complete logic for that authentication scenario.
/// </summary>
public interface IFlowHandler
{
    /// <summary>
    /// The <see cref="FlowType"/> this handler processes.
    /// Used by the dispatcher to route callbacks to the correct handler.
    /// </summary>
    FlowType HandledFlowType { get; }

    /// <summary>
    /// Executes the flow logic for the given callback context.
    /// </summary>
    /// <param name="context">
    /// The callback context containing the authorization code, PKCE code verifier,
    /// redirect URI, consumed flow state, and host-resolved tenant.
    /// </param>
    /// <returns>A <see cref="FlowResult"/> indicating the outcome of the flow.</returns>
    Task<FlowResult> HandleCallbackAsync(FlowCallbackContext context);
}
