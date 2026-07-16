using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Api.Controllers;

/// <summary>
/// Thin HTTP adapter for all authentication endpoints. Contains ZERO business logic —
/// all operations are delegated to service layer interfaces.
/// </summary>
[Route("auth")]
[ApiController]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthFlowService _authFlowService;
    private readonly IAuthSessionService _authSessionService;
    private readonly IAuthCookieWriter _authCookieWriter;
    private readonly IUserRepository _userRepository;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IOptions<KeycloakOptions> _keycloakOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAntiforgery? _antiforgery;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthController"/>.
    /// </summary>
    /// <param name="authFlowService">Orchestrates authentication flow initiation and callback dispatch.</param>
    /// <param name="authSessionService">Manages tenant selection and token refresh.</param>
    /// <param name="authCookieWriter">Writes and clears authentication cookies.</param>
    /// <param name="userRepository">Resolves GroundUp users by external identity provider identifier.</param>
    /// <param name="authOptions">Authentication configuration options.</param>
    /// <param name="keycloakOptions">Keycloak identity provider configuration.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients (used for Keycloak end_session).</param>
    /// <param name="antiforgery">Optional antiforgery service for CSRF token provisioning.</param>
    public AuthController(
        IAuthFlowService authFlowService,
        IAuthSessionService authSessionService,
        IAuthCookieWriter authCookieWriter,
        IUserRepository userRepository,
        IOptions<AuthOptions> authOptions,
        IOptions<KeycloakOptions> keycloakOptions,
        IHttpClientFactory httpClientFactory,
        IAntiforgery? antiforgery = null)
    {
        _authFlowService = authFlowService;
        _authSessionService = authSessionService;
        _authCookieWriter = authCookieWriter;
        _userRepository = userRepository;
        _authOptions = authOptions;
        _keycloakOptions = keycloakOptions;
        _httpClientFactory = httpClientFactory;
        _antiforgery = antiforgery;
    }

    /// <summary>
    /// Initiates the standard login flow. Redirects the user to Keycloak for authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 302 redirect to the Keycloak authorization endpoint.</returns>
    [HttpGet("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken = default)
    {
        var request = new FlowInitiationRequest(FlowType.Login);
        var result = await _authFlowService.InitiateFlowAsync(request, HttpContext, cancellationToken);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { error = result.Message });
        }

        return Redirect(result.Data!.RedirectUrl);
    }

    /// <summary>
    /// Initiates the new organization registration flow. Redirects the user to Keycloak
    /// for authentication/registration with the organization name preserved for callback processing.
    /// </summary>
    /// <param name="organizationName">The name of the organization to create (required, max 200 characters).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 302 redirect to the Keycloak authorization endpoint.</returns>
    [HttpGet("register")]
    public async Task<IActionResult> Register(
        [FromQuery] string? organizationName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            return BadRequest(new { error = "Organization name is required." });
        }

        if (organizationName.Length > 200)
        {
            return BadRequest(new { error = "Organization name must not exceed 200 characters." });
        }

        var request = new FlowInitiationRequest(FlowType.NewOrganization, OrganizationName: organizationName);
        var result = await _authFlowService.InitiateFlowAsync(request, HttpContext, cancellationToken);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { error = result.Message });
        }

        return Redirect(result.Data!.RedirectUrl);
    }

    /// <summary>
    /// Handles the OAuth callback from Keycloak. Dispatches to the appropriate flow handler
    /// based on the stored <c>AuthFlowState</c>.
    /// </summary>
    /// <param name="code">The authorization code returned by Keycloak.</param>
    /// <param name="state">The state token for CSRF validation and flow correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// HTTP 302 redirect on success; HTTP 200 JSON with tenant list on pending selection;
    /// appropriate error status on failure.
    /// </returns>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new { error = "Missing code or state parameter." });
        }

        var result = await _authFlowService.HandleCallbackAsync(code, state, HttpContext, cancellationToken);

        if (!result.Success)
        {
            var statusCode = result.StatusCode > 0 ? result.StatusCode : 400;
            return StatusCode(statusCode, new { error = result.Message });
        }

        var flowResult = result.Data!;

        if (!flowResult.IsSuccess)
        {
            var statusCode = flowResult.HttpStatus ?? 400;
            return StatusCode(statusCode, new { error = flowResult.ErrorCode, message = flowResult.ErrorMessage });
        }

        if (flowResult.RequiresTenantSelection)
        {
            ProvisionAntiforgeryToken();
            return Ok(new { tenants = flowResult.TenantList });
        }

        // Success with token — cookie already written by the flow handler
        ProvisionAntiforgeryToken();
        var redirectUrl = flowResult.RedirectUrl ?? "/";
        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Returns the current authenticated user's identity and session information.
    /// </summary>
    /// <returns>
    /// HTTP 200 with user claims when authenticated; HTTP 401 when unauthenticated.
    /// For pending-selection principals (Keycloak token, no tid), returns identity with null tenant.
    /// </returns>
    [HttpGet("me")]
    public IActionResult Me()
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var options = _authOptions.Value;
        var subClaim = HttpContext.User.FindFirst(options.UserIdClaimType)?.Value
                       ?? HttpContext.User.FindFirst("sub")?.Value;
        var email = HttpContext.User.FindFirst(options.EmailClaimType)?.Value
                    ?? HttpContext.User.FindFirst("email")?.Value;
        var displayName = HttpContext.User.FindFirst(options.DisplayNameClaimType)?.Value
                          ?? HttpContext.User.FindFirst("name")?.Value;
        var tidClaim = HttpContext.User.FindFirst(options.TenantIdClaimType)?.Value;

        // Check if this is a full GroundUp token (has tid) or a pending-selection Keycloak token
        Guid? tenantId = null;
        if (!string.IsNullOrEmpty(tidClaim) && Guid.TryParse(tidClaim, out var parsedTenantId))
        {
            tenantId = parsedTenantId;
        }

        Guid? userId = null;
        if (!string.IsNullOrEmpty(subClaim) && Guid.TryParse(subClaim, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        var roles = HttpContext.User.FindAll("role")
            .Select(c => c.Value)
            .ToList();

        ProvisionAntiforgeryToken();

        return Ok(new
        {
            userId = userId?.ToString() ?? subClaim,
            email,
            displayName,
            tenantId,
            roles
        });
    }

    /// <summary>
    /// Selects a tenant for the authenticated user's session. Resolves the GroundUp user
    /// from the principal (by external sub for Keycloak tokens, by userId for GroundUp tokens)
    /// and delegates to the session service.
    /// </summary>
    /// <param name="request">The tenant selection request containing the target tenant ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 on success; HTTP 403 if the user is not a member of the requested tenant.</returns>
    [HttpPost("set-tenant")]
    public async Task<IActionResult> SetTenant(
        [FromBody] SetTenantRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var options = _authOptions.Value;
        var tidClaim = HttpContext.User.FindFirst(options.TenantIdClaimType)?.Value;
        var hasTid = !string.IsNullOrEmpty(tidClaim) && Guid.TryParse(tidClaim, out _);

        Guid userId;
        DateTimeOffset? originalAuthTime = null;

        if (!hasTid)
        {
            // Pending-selection Keycloak principal: resolve GroundUp user by external sub
            var subClaim = HttpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(subClaim))
            {
                return Unauthorized();
            }

            var userResult = await _userRepository.GetByExternalUserIdAsync(subClaim, cancellationToken);
            if (!userResult.Success || userResult.Data is null)
            {
                return NotFound(new { error = "User not found." });
            }

            userId = userResult.Data.Id;
            // First issuance: originalAuthTime is null → service sets auth_time = now
        }
        else
        {
            // Existing GroundUp token: read userId and preserve auth_time
            var userIdClaim = HttpContext.User.FindFirst(options.UserIdClaimType)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
            {
                return Unauthorized();
            }

            // Read auth_time from the current token to preserve on reissue
            var authTimeClaim = HttpContext.User.FindFirst("auth_time");
            if (authTimeClaim is not null && long.TryParse(authTimeClaim.Value, out var authTimeUnix))
            {
                originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
            }
        }

        var result = await _authSessionService.SetTenantAsync(userId, request.TenantId, originalAuthTime);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { error = result.Message });
        }

        var response = result.Data!;

        if (response.SelectionRequired)
        {
            return Ok(new { tenants = response.AvailableTenants });
        }

        if (!string.IsNullOrEmpty(response.Token))
        {
            _authCookieWriter.WriteAuthCookie(HttpContext, response.Token);
        }

        return Ok();
    }

    /// <summary>
    /// Triggers an explicit token refresh. Re-validates membership and reissues a fresh token
    /// with preserved auth_time.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 on success; HTTP 403 if membership has been revoked.</returns>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var options = _authOptions.Value;

        // Read userId
        var userIdClaim = HttpContext.User.FindFirst(options.UserIdClaimType)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        // Read tenantId
        var tidClaim = HttpContext.User.FindFirst(options.TenantIdClaimType)?.Value;
        if (string.IsNullOrEmpty(tidClaim) || !Guid.TryParse(tidClaim, out var tenantId))
        {
            return Unauthorized();
        }

        // Read auth_time from the current token
        var authTimeClaim = HttpContext.User.FindFirst("auth_time");
        if (authTimeClaim is null || !long.TryParse(authTimeClaim.Value, out var authTimeUnix))
        {
            return BadRequest(new { error = "Missing auth_time claim." });
        }

        var originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);

        var result = await _authSessionService.RefreshTokenAsync(userId, tenantId, originalAuthTime);

        if (!result.Success)
        {
            _authCookieWriter.ClearAuthCookie(HttpContext);
            return StatusCode(403, new { error = result.Message });
        }

        if (!string.IsNullOrEmpty(result.Data))
        {
            _authCookieWriter.WriteAuthCookie(HttpContext, result.Data);
        }

        return Ok();
    }

    /// <summary>
    /// Clears the local authentication cookie and terminates the Keycloak SSO session
    /// via the RP-initiated logout endpoint. Uses <c>client_id</c> and <c>post_logout_redirect_uri</c>
    /// (no <c>id_token_hint</c> in Phase 10C).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 after cookie is cleared and Keycloak session is terminated.</returns>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        _authCookieWriter.ClearAuthCookie(HttpContext);

        // Call Keycloak end_session_endpoint to terminate the SSO session
        var keycloakOpts = _keycloakOptions.Value;
        var endSessionUrl = $"{keycloakOpts.PublicBaseUrl}/realms/{keycloakOpts.SharedRealmName}/protocol/openid-connect/logout";

        var postLogoutRedirectUri = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/";

        try
        {
            var client = _httpClientFactory.CreateClient("KeycloakIdp");
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = keycloakOpts.AppClientId,
                ["post_logout_redirect_uri"] = postLogoutRedirectUri
            };

            using var content = new FormUrlEncodedContent(parameters);
            await client.PostAsync(endSessionUrl, content, cancellationToken);
        }
        catch
        {
            // Best-effort: if end_session fails, the local cookie is already cleared.
            // The user is logged out of GroundUp regardless.
        }

        return Ok();
    }

    /// <summary>
    /// Provisions the antiforgery cookie token so that the client can supply the request token
    /// on subsequent state-changing requests (POST /auth/set-tenant, refresh, logout).
    /// </summary>
    private void ProvisionAntiforgeryToken()
    {
        if (_antiforgery is not null)
        {
            _antiforgery.GetAndStoreTokens(HttpContext);
        }
    }
}
