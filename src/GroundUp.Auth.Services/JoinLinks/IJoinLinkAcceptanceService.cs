using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services.JoinLinks;

/// <summary>
/// The public side of join links (GU-68 Requirement 9.3): what the link shows, and starting the
/// sign-in that joins. The token is the credential, so these methods need no signed-in user. The
/// callback is handled by the join-link flow handler.
/// </summary>
public interface IJoinLinkAcceptanceService
{
    /// <summary>Describes the link behind <paramref name="token"/>. An unusable one only gives the reason.</summary>
    Task<OperationResult<JoinLinkLookupDto>> LookupAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the sign-in that joins the link's organization and returns the identity provider address.
    /// A join link has no email, so the page is the sign-in page (which links to registration), unless
    /// <paramref name="forceLogin"/> asks for credentials even with a live single sign-on session.
    /// </summary>
    /// <returns>
    /// The flow; 404 for an unknown token; 410 <c>JOIN_LINK_NO_LONGER_VALID</c>; 403
    /// <c>INVITATION_WRONG_ORGANIZATION</c> on another tenant's host; 422 <c>ENTERPRISE_NOT_SUPPORTED</c>.
    /// </returns>
    Task<OperationResult<FlowInitiationResult>> StartAsync(
        string token, bool forceLogin, HttpContext httpContext, CancellationToken cancellationToken = default);
}
