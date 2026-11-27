using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Services.JoinLinks;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// The public join-link endpoints (GU-68 Requirement 9.3): what a join link shows, and starting the
/// sign-in that joins. The token is the credential and goes in the body. Under <c>/auth</c>, so the
/// host/token check does not stop someone signed in to another tenant.
/// </summary>
[ApiController]
[Route("auth/join-links")]
public sealed class JoinLinkAcceptanceController : ControllerBase
{
    private readonly IJoinLinkAcceptanceService _acceptanceService;

    /// <summary>
    /// Initializes a new instance of <see cref="JoinLinkAcceptanceController"/>.
    /// </summary>
    public JoinLinkAcceptanceController(IJoinLinkAcceptanceService acceptanceService)
    {
        _acceptanceService = acceptanceService;
    }

    /// <summary>
    /// Describes the link: organization and role when usable, otherwise only the reason
    /// (<c>not_found</c>, <c>revoked</c>, <c>expired</c>, <c>used_up</c>).
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
    /// Starts the sign-in that joins the organization. The client navigates to the returned
    /// <c>redirectUrl</c>; the identity provider returns to <c>/auth/callback</c>, which then redirects to
    /// <c>returnUrl</c> (an optional path on this site) with the outcome.
    /// </summary>
    [HttpPost("join")]
    public async Task<IActionResult> Join(
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
