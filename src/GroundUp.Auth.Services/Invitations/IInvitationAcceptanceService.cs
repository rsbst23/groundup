using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services.Invitations;

/// <summary>
/// The public side of invitations (GU-68 Requirement 7): what the link shows, and starting the
/// sign-in that accepts it. The token is the credential, so these methods need no signed-in user.
/// The callback is handled by the invitation flow handler.
/// </summary>
public interface IInvitationAcceptanceService
{
    /// <summary>
    /// Describes the invitation behind <paramref name="token"/>. An unusable one only gives the reason.
    /// </summary>
    Task<OperationResult<InvitationLookupDto>> LookupAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the sign-in that accepts the invitation and returns the identity provider address. The
    /// server picks the page: sign-in when the invited email has an account, registration when not,
    /// a forced sign-in when the current session belongs to another email or <paramref name="forceLogin"/>
    /// is set. The invited email is pre-filled. The response does not reveal whether an account exists.
    /// <paramref name="returnUrl"/> is an optional path on this site the callback redirects to (GU-87).
    /// </summary>
    /// <returns>
    /// The flow; 404 for an unknown token; 410 <c>INVITATION_NO_LONGER_VALID</c> or
    /// <c>INVITATION_ALREADY_USED</c>; 403 <c>INVITATION_WRONG_ORGANIZATION</c> on another tenant's host;
    /// 422 <c>ENTERPRISE_NOT_SUPPORTED</c>.
    /// </returns>
    Task<OperationResult<FlowInitiationResult>> StartAsync(
        string token, bool forceLogin, string? returnUrl, HttpContext httpContext, CancellationToken cancellationToken = default);
}
