namespace GroundUp.Api.Controllers.Setup;

using GroundUp.Api.Authentication;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Results;
using GroundUp.Services.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Setup wizard endpoints. All endpoints except GET /setup require
/// BootstrapAdminToken authentication. Rate-limited per IP.
/// </summary>
[ApiController]
[Route("setup")]
public sealed class SetupController : ControllerBase
{
    private readonly ISetupWizardService _wizardService;

    public SetupController(ISetupWizardService wizardService)
    {
        _wizardService = wizardService;
    }

    /// <summary>
    /// Public landing page — confirms setup mode is active.
    /// Returns 404 once setup is complete.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetSetupLanding(
        [FromServices] IBootstrapStateService bootstrap, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await bootstrap.IsCompleteAsync(ct))
            return NotFound();

        return Ok(new
        {
            code = "setup_active",
            message = "Setup mode active. Authenticate with the bootstrap admin token and call GET /setup/status to inspect wizard state."
        });
    }

    /// <summary>Returns the current wizard status.</summary>
    [HttpGet("status")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var result = await _wizardService.GetStatusAsync(ct);
        return ToActionResult(result);
    }

    /// <summary>Sets the application identity (name + domain).</summary>
    [HttpPost("app-identity")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> SetAppIdentity([FromBody] SetAppIdentityRequest request, CancellationToken ct)
    {
        var result = await _wizardService.SetAppIdentityAsync(request, ct);
        return ToActionResult(result);
    }

    /// <summary>Configures the identity provider URLs and realm.</summary>
    [HttpPost("identity-provider")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> SetIdentityProvider([FromBody] SetIdentityProviderRequest request, CancellationToken ct)
    {
        var result = await _wizardService.SetIdentityProviderAsync(request, ct);
        return ToActionResult(result);
    }

    /// <summary>Bootstraps the Keycloak admin client.</summary>
    [HttpPost("keycloak-bootstrap")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> BootstrapKeycloak([FromBody] KeycloakBootstrapRequest request, CancellationToken ct)
    {
        var operatorIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _wizardService.BootstrapKeycloakAsync(request, operatorIp, ct);
        return ToActionResult(result);
    }

    /// <summary>Creates the first super admin user.</summary>
    [HttpPost("first-admin")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> CreateFirstAdmin([FromBody] CreateFirstAdminRequest request, CancellationToken ct)
    {
        var operatorIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        var result = await _wizardService.CreateFirstAdminAsync(request, operatorIp, correlationId, ct);
        return ToActionResult(result);
    }

    /// <summary>Marks setup as complete.</summary>
    [HttpPost("complete")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> CompleteSetup(CancellationToken ct)
    {
        var result = await _wizardService.CompleteSetupAsync(ct);
        if (result.Success)
        {
            // Req 14.7: include redirectTo in the response
            return Ok(new { step = "complete", completed = true, redirectTo = "/" });
        }
        return ToActionResult(result);
    }

    /// <summary>Returns the most recent transaction log entries.</summary>
    [HttpGet("transaction-log")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> GetTransactionLog(CancellationToken ct)
    {
        var result = await _wizardService.GetTransactionLogAsync(ct);
        return ToActionResult(result);
    }

    /// <summary>Recovers a partially-failed setup step.</summary>
    [HttpPost("recover/{transactionLogId:guid}")]
    [Authorize(AuthenticationSchemes = BootstrapAdminTokenAuthenticationHandler.SchemeName)]
    public async Task<IActionResult> Recover(Guid transactionLogId, CancellationToken ct)
    {
        var result = await _wizardService.RecoverAsync(transactionLogId, ct);
        return ToActionResult(result);
    }

    // ─── Helper ────────────────────────────────────────────────────────────────

    private IActionResult ToActionResult<T>(OperationResult<T> result)
    {
        if (result.Success)
            return Ok(result.Data);

        return StatusCode(result.StatusCode, new
        {
            code = result.ErrorCode ?? "error",
            message = result.Message
        });
    }

    private IActionResult ToActionResult(OperationResult result)
    {
        if (result.Success)
            return Ok();

        return StatusCode(result.StatusCode, new
        {
            code = result.ErrorCode ?? "error",
            message = result.Message
        });
    }
}
