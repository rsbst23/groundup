using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Auth.Services;

/// <summary>
/// Context passed to an <see cref="IFlowHandler"/> when dispatching an OAuth callback.
/// Contains all information the handler needs to complete the flow: the authorization code
/// for token exchange, the PKCE code verifier, the original redirect URI, the consumed
/// flow state with security metadata, and the host-resolved tenant (if any).
/// </summary>
/// <param name="AuthorizationCode">The OAuth2 authorization code received in the callback.</param>
/// <param name="CodeVerifier">The PKCE code_verifier stored at flow initiation, used for code exchange.</param>
/// <param name="RedirectUri">The exact redirect_uri used at authorize time (must match at code exchange).</param>
/// <param name="ConsumedState">The consumed <see cref="AuthFlowStateDto"/> containing stored nonce, flow type, and metadata.</param>
/// <param name="HostResolvedTenant">The tenant resolved from the request Host header, or null if none was resolved.</param>
public sealed record FlowCallbackContext(
    string AuthorizationCode,
    string CodeVerifier,
    string RedirectUri,
    AuthFlowStateDto ConsumedState,
    TenantDto? HostResolvedTenant);
