using GroundUp.Api.Controllers;
using GroundUp.Core.Results;
using GroundUp.Sample.Dev;
using Microsoft.AspNetCore.Mvc;

namespace GroundUp.Sample.Controllers;

/// <summary>
/// Development tools of the sample app (GU-90). Not part of the framework. Every action is 404 unless
/// the app runs in Development (the tools are only registered there).
/// </summary>
[Route("api/dev")]
public sealed class DevController : BaseController
{
    private readonly DevMailbox? _mailbox;

    /// <summary>
    /// Initializes a new instance of <see cref="DevController"/>. The mailbox is null outside Development.
    /// </summary>
    public DevController(IServiceProvider services)
    {
        _mailbox = services.GetService<DevMailbox>();
    }

    /// <summary>
    /// The invitation emails kept instead of being sent, newest first (the last 50). Needs a signed-in
    /// user. The links contain invitation tokens, which work like passwords: a development tool only.
    /// </summary>
    [HttpGet("invitation-emails")]
    public ActionResult<OperationResult<IReadOnlyList<DevEmail>>> InvitationEmails()
    {
        if (_mailbox is null)
        {
            return NotFound();
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        return ToActionResult(OperationResult<IReadOnlyList<DevEmail>>.Ok(_mailbox.List()));
    }
}
