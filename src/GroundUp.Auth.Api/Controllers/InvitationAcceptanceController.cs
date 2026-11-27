using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.Invitations;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// The public invitation endpoints (GU-68 Requirement 7): what an invitation link shows, and
/// starting the sign-in that accepts it. No signed-in user is needed: the token is the credential.
/// Under <c>/auth</c>, so an invitee signed in to another tenant (or on another tenant's host) is not
/// stopped by the host/token check. Cookie-authenticated calls send <c>X-CSRF-Token</c> as usual.
/// </summary>
[ApiController]
[Route("auth/invitations")]
public sealed class InvitationAcceptanceController : ControllerBase
{
    private readonly IInvitationAcceptanceService _acceptanceService;

    /// <summary>
    /// Initializes a new instance of <see cref="InvitationAcceptanceController"/>.
    /// </summary>
    public InvitationAcceptanceController(IInvitationAcceptanceService acceptanceService)
    {
        _acceptanceService = acceptanceService;
    }

    /// <summary>
    /// Describes the invitation: organization and role when usable, otherwise only the reason
    /// (<c>not_found</c>, <c>revoked</c>, <c>expired</c>, <c>accepted</c>).
    /// </summary>
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup([FromBody] InvitationTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = await _acceptanceService.LookupAsync(request.Token ?? string.Empty, cancellationToken);
        return result.Success
            ? Ok(result.Data)
            : StatusCode(result.StatusCode, new { error = result.Message, errorCode = result.ErrorCode });
    }

    /// <summary>
    /// Starts the sign-in that accepts the invitation. The client navigates to the returned
    /// <c>redirectUrl</c> straight away; the identity provider returns to <c>/auth/callback</c>, which then
    /// redirects to <c>returnUrl</c> (an optional path on this site) with the outcome.
    /// </summary>
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(
        [FromBody] InvitationTokenRequestDto request,
        [FromQuery] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _acceptanceService.StartAsync(request.Token ?? string.Empty, request.ForceLogin, returnUrl, HttpContext, cancellationToken);
        return result.Success
            ? Ok(new { redirectUrl = result.Data!.RedirectUrl })
            : StatusCode(result.StatusCode, new { error = result.Message, errorCode = result.ErrorCode });
    }
}
