using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services;

/// <summary>
/// Orchestrator service for authentication flows. Initiates callback-based flows
/// (creating <c>AuthFlowState</c> rows with security metadata) and dispatches
/// OAuth callbacks to the appropriate <see cref="IFlowHandler"/> based on the
/// stored <c>FlowType</c>.
/// </summary>
public interface IAuthFlowService
{
    /// <summary>
    /// Initiates an auth flow: creates AuthFlowState, builds redirect URL, sets state cookie.
    /// </summary>
    /// <param name="request">The flow initiation parameters including flow type and optional metadata.</param>
    /// <param name="httpContext">The current HTTP context (used to set the state cookie and read client info).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the redirect URL and flow state ID on success.</returns>
    Task<OperationResult<FlowInitiationResult>> InitiateFlowAsync(
        FlowInitiationRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches an OAuth callback to the appropriate flow handler.
    /// Validates state cookie binding, consumes the AuthFlowState, routes to handler.
    /// </summary>
    /// <param name="code">The authorization code returned by the identity provider.</param>
    /// <param name="state">The state token returned in the callback query parameter.</param>
    /// <param name="httpContext">The current HTTP context (used to read the state cookie).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the flow result on success.</returns>
    Task<OperationResult<FlowResult>> HandleCallbackAsync(
        string code,
        string state,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
