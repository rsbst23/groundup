using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Membership;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
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
    private readonly IMembershipService _membershipService;
    private readonly IOrganizationProvisioningService _organizationProvisioningService;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly IOptions<KeycloakOptions> _keycloakOptions;
    private readonly IAntiforgery _antiforgery;
    private readonly IOptions<AntiforgeryOptions> _antiforgeryOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthController"/>.
    /// </summary>
    /// <param name="authFlowService">Orchestrates authentication flow initiation and callback dispatch.</param>
    /// <param name="authSessionService">Manages tenant selection and token refresh.</param>
    /// <param name="authCookieWriter">Writes and clears authentication cookies.</param>
    /// <param name="membershipService">Lists the user's tenants and ends memberships (leave).</param>
    /// <param name="organizationProvisioningService">Creates organizations for authenticated users.</param>
    /// <param name="authOptions">Authentication configuration options.</param>
    /// <param name="keycloakOptions">Keycloak identity provider configuration.</param>
    /// <param name="antiforgery">Issues the CSRF request token returned by <c>GET /auth/csrf</c>.</param>
    /// <param name="antiforgeryOptions">Antiforgery cookie settings, used to clear the cookie at logout.</param>
    public AuthController(
        IAuthFlowService authFlowService,
        IAuthSessionService authSessionService,
        IAuthCookieWriter authCookieWriter,
        IMembershipService membershipService,
        IOrganizationProvisioningService organizationProvisioningService,
        IOptions<AuthOptions> authOptions,
        IOptions<KeycloakOptions> keycloakOptions,
        IAntiforgery antiforgery,
        IOptions<AntiforgeryOptions> antiforgeryOptions)
    {
        _authFlowService = authFlowService;
        _authSessionService = authSessionService;
        _authCookieWriter = authCookieWriter;
        _membershipService = membershipService;
        _organizationProvisioningService = organizationProvisioningService;
        _authOptions = authOptions;
        _keycloakOptions = keycloakOptions;
        _antiforgery = antiforgery;
        _antiforgeryOptions = antiforgeryOptions;
    }

    /// <summary>
    /// Initiates the standard login flow. Returns the Keycloak authorization URL as JSON
    /// for the client to navigate to.
    /// </summary>
    /// <param name="forceLogin">
    /// When true, the identity provider asks for credentials even if its single sign-on session is
    /// still alive. Use after a <c>401 REAUTH_REQUIRED</c> response.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with <c>{ redirectUrl }</c> pointing to the Keycloak authorization endpoint.</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromQuery] bool forceLogin = false,
        CancellationToken cancellationToken = default)
    {
        var request = new FlowInitiationRequest(FlowType.Login, ForceLogin: forceLogin);
        var result = await _authFlowService.InitiateFlowAsync(request, HttpContext, cancellationToken);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { error = result.Message });
        }

        return Ok(new { redirectUrl = result.Data!.RedirectUrl });
    }

    /// <summary>
    /// Initiates a signup flow. When <c>organizationName</c> is provided, starts a new-organization
    /// registration; when omitted (null), starts a simple signup that joins the default tenant.
    /// Returns the Keycloak authorization URL as JSON for the client to navigate to.
    /// </summary>
    /// <param name="request">Optional request body containing the organization name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with <c>{ redirectUrl }</c> pointing to the Keycloak authorization endpoint.</returns>
    [HttpPost("signup")]
    public async Task<IActionResult> Signup(
        [FromBody] SignupRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var organizationName = request?.OrganizationName;

        if (organizationName is not null && organizationName.Length > 200)
        {
            return BadRequest(new { error = "Organization name must not exceed 200 characters." });
        }

        var flowType = string.IsNullOrWhiteSpace(organizationName)
            ? FlowType.SimpleSignup
            : FlowType.NewOrganization;

        var flowRequest = new FlowInitiationRequest(flowType, OrganizationName: organizationName);
        var result = await _authFlowService.InitiateFlowAsync(flowRequest, HttpContext, cancellationToken);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { error = result.Message });
        }

        return Ok(new { redirectUrl = result.Data!.RedirectUrl });
    }

    /// <summary>
    /// Handles the OAuth callback from Keycloak. Dispatches to the appropriate flow handler
    /// based on the stored <c>AuthFlowState</c>.
    /// </summary>
    /// <param name="code">The authorization code returned by Keycloak.</param>
    /// <param name="state">The state token for CSRF validation and flow correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// HTTP 200 JSON on success (token + session info or tenant selection);
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
            // errorCode is the same value as error; it matches the shape of the other auth endpoints
            // (for example errorCode REAUTH_REQUIRED tells the client to call login?forceLogin=true).
            return StatusCode(statusCode, new
            {
                error = flowResult.ErrorCode,
                errorCode = flowResult.ErrorCode,
                message = flowResult.ErrorMessage
            });
        }

        if (flowResult.IsTenantless)
        {
            // Signed in with no current tenant. The cookie holds a tenantless token (written by the
            // handler). requiresTenantSelection is true when there is something to choose from;
            // false with an empty list means the user has no tenant yet (they can create one).
            // The client gets its CSRF token from GET /auth/csrf after this response.
            return Ok(new
            {
                requiresTenantSelection = flowResult.RequiresTenantSelection,
                tenants = flowResult.TenantList ?? new List<TenantListItemDto>()
            });
        }

        // Success with token — cookie already written by the flow handler.
        // The client gets its CSRF token from GET /auth/csrf after this response.
        // alreadyMember: an invitation or join link used by an existing member (no role change)
        return Ok(new { token = flowResult.Token, redirectUrl = flowResult.RedirectUrl ?? "/", alreadyMember = flowResult.AlreadyMember });
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
    /// Issues the CSRF request token for a signed-in browser client. The response also sets
    /// (or refreshes) the HttpOnly antiforgery cookie the token is paired with.
    /// <para>
    /// Client flow: call this once after sign-in, keep the token in memory, and send it as the
    /// <c>X-CSRF-Token</c> header on every POST, PUT, PATCH and DELETE. On a
    /// <c>403 CSRF_VALIDATION_FAILED</c>, call this again and retry once. Clients that send an
    /// <c>Authorization: Bearer</c> header do not need it.
    /// </para>
    /// </summary>
    /// <returns>HTTP 200 with <c>{ token }</c>; HTTP 401 when unauthenticated.</returns>
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store";

        return Ok(new { token = tokens.RequestToken });
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

        OperationResult<SetTenantResponseDto> result;

        if (HttpContext.User.IsExternalIdp())
        {
            // Identity-provider principal (for example a Keycloak Bearer token): its sub is the
            // provider's user id. The service resolves the GroundUp user (404 when there is none).
            var subClaim = HttpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(subClaim))
            {
                return Unauthorized();
            }

            result = await _authSessionService.SetTenantForExternalUserAsync(subClaim, request.TenantId, cancellationToken);
            if (!result.Success && result.ErrorCode == ErrorCodes.UserNotFound)
            {
                // Unchanged response for an identity-provider user with no GroundUp account
                return NotFound(new { error = result.Message });
            }
        }
        else
        {
            // GroundUp token, with or without a current tenant: read userId and preserve auth_time
            var userIdClaim = HttpContext.User.FindFirst(_authOptions.Value.UserIdClaimType)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            DateTimeOffset? originalAuthTime = null;
            var authTimeClaim = HttpContext.User.FindFirst("auth_time");
            if (authTimeClaim is not null && long.TryParse(authTimeClaim.Value, out var authTimeUnix))
            {
                originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
            }

            result = await _authSessionService.SetTenantAsync(userId, request.TenantId, originalAuthTime);
        }

        if (!result.Success)
        {
            // errorCode lets the client tell "sign in again" (REAUTH_REQUIRED) from other failures
            return StatusCode(result.StatusCode, new { error = result.Message, errorCode = result.ErrorCode });
        }

        var response = result.Data!;

        if (response.SelectionRequired)
        {
            return Ok(new { tenants = response.AvailableTenants });
        }

        if (!string.IsNullOrEmpty(response.Token))
        {
            await _authCookieWriter.WriteAuthCookieAsync(HttpContext, response.Token);
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
            await _authCookieWriter.ClearAuthCookieAsync(HttpContext);

            if (result.ErrorCode == ErrorCodes.ReauthRequired)
            {
                // The session reached its absolute lifetime: the client must start a forced login.
                return StatusCode(401, new { error = result.Message, errorCode = result.ErrorCode });
            }

            return StatusCode(403, new { error = result.Message });
        }

        if (!string.IsNullOrEmpty(result.Data))
        {
            await _authCookieWriter.WriteAuthCookieAsync(HttpContext, result.Data);
        }

        return Ok();
    }

    /// <summary>
    /// Clears the local authentication cookie and returns the Keycloak RP-initiated logout URL
    /// for the client to navigate to. The browser must visit this URL to terminate the Keycloak
    /// SSO session — a server-to-server call cannot clear the browser's Keycloak session cookie.
    /// When the Keycloak id_token is available (stored as <c>IdToken</c> cookie), it is included
    /// as <c>id_token_hint</c> to suppress Keycloak's logout confirmation screen.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// HTTP 200 with <c>{ logoutUrl }</c> after cookie is cleared.
    /// The client should navigate the browser to <c>logoutUrl</c> to complete SSO logout.
    /// </returns>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var authOptions = _authOptions.Value;
        var keycloakOpts = _keycloakOptions.Value;

        // Clear both the auth cookie and the id_token hint cookie
        await _authCookieWriter.ClearAuthCookieAsync(HttpContext);
        await _authCookieWriter.ClearIdTokenCookieAsync(HttpContext);

        // The antiforgery cookie is bound to the signed-out identity; remove it too.
        var antiforgeryCookie = _antiforgeryOptions.Value.Cookie;
        if (!string.IsNullOrEmpty(antiforgeryCookie.Name))
        {
            Response.Cookies.Delete(antiforgeryCookie.Name, new CookieOptions
            {
                Path = antiforgeryCookie.Path ?? "/",
                Domain = antiforgeryCookie.Domain,
                HttpOnly = true,
                Secure = authOptions.CookieSecure,
                SameSite = SameSiteMode.Strict
            });
        }

        var postLogoutRedirectUri = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}/";

        var logoutUrl = $"{keycloakOpts.PublicBaseUrl}/realms/{keycloakOpts.SharedRealmName}" +
                        $"/protocol/openid-connect/logout" +
                        $"?client_id={Uri.EscapeDataString(keycloakOpts.AppClientId)}" +
                        $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirectUri)}";

        // Include id_token_hint when available — this suppresses Keycloak's logout confirmation
        var idToken = HttpContext.Request.Cookies[authOptions.IdTokenCookieName];
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            logoutUrl += $"&id_token_hint={Uri.EscapeDataString(idToken)}";
        }

        return Ok(new { logoutUrl });
    }

    /// <summary>
    /// Returns the list of tenants the authenticated user is a member of.
    /// For standard tenants, includes a <c>current</c> flag and <c>switchable: true</c>.
    /// For enterprise tenants (with a RealmName), marks them as <c>switchable: false</c>
    /// since they require re-authentication via their subdomain.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP 200 with the tenant list; HTTP 401 when unauthenticated.</returns>
    [HttpGet("me/tenants")]
    public async Task<IActionResult> GetMyTenants(CancellationToken cancellationToken = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        // Only a GroundUp token identifies a GroundUp user; an identity-provider token's sub does not
        if (ResolveGroundUpUserId() is null)
        {
            return Unauthorized();
        }

        var result = await _membershipService.GetMyTenantsAsync(cancellationToken);
        if (!result.Success || result.Data is null)
        {
            return result.StatusCode == 401 ? Unauthorized() : Ok(Array.Empty<object>());
        }

        // Two response shapes, unchanged: enterprise tenants carry subdomain, standard tenants carry current
        var tenantList = result.Data.Select(t => t.Type == "enterprise"
            ? (object)new { id = t.Id, name = t.Name, slug = t.Slug, type = t.Type, switchable = t.Switchable, subdomain = t.Subdomain }
            : new { id = t.Id, name = t.Name, slug = t.Slug, type = t.Type, switchable = t.Switchable, current = t.Current ?? false })
            .ToList();

        return Ok(tenantList);
    }

    /// <summary>
    /// Creates an additional standard organization for the authenticated user, who becomes its
    /// TenantAdmin. No identity-provider round-trip is made. The session stays scoped to the
    /// current tenant (or lack of one); the user enters the new organization with <c>POST /auth/set-tenant</c>.
    /// </summary>
    /// <param name="request">The request body containing the organization name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// HTTP 201 with <c>{ id, name, slug }</c> on success;
    /// HTTP 400 when the name is missing, blank or longer than 200 characters;
    /// HTTP 401 when unauthenticated, when the principal is an identity-provider token, or when the user cannot be resolved;
    /// HTTP 409 with <c>SLUG_COLLISION</c> when no unique slug could be found.
    /// </returns>
    [HttpPost("organizations")]
    public async Task<IActionResult> CreateOrganization(
        [FromBody] CreateOrganizationRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        // A GroundUp session is required, with or without a current tenant (a tenantless user can
        // create their first organization). An identity-provider token has no GroundUp user id.
        var options = _authOptions.Value;
        if (HttpContext.User.IsExternalIdp())
        {
            return Unauthorized();
        }

        var userIdClaim = HttpContext.User.FindFirst(options.UserIdClaimType)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out _))
        {
            return Unauthorized();
        }

        var result = await _organizationProvisioningService.CreateForCurrentUserAsync(
            request ?? new CreateOrganizationRequestDto(), cancellationToken);

        if (!result.Success || result.Data is null)
        {
            return result.StatusCode switch
            {
                401 => Unauthorized(),
                400 => BadRequest(new { error = result.Message }),
                _ => StatusCode(result.StatusCode, new { error = result.Message, errorCode = result.ErrorCode })
            };
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            id = result.Data.TenantId,
            name = result.Data.Name,
            slug = result.Data.Slug
        });
    }

    /// <summary>
    /// Leaves a tenant. Deactivates the user's membership in the specified tenant.
    /// If the user is the sole TenantAdmin, returns 403 (last-admin guard).
    /// If the user was working in the left tenant, the session becomes tenantless (identity only);
    /// they are not moved into another tenant. Leaving a different tenant leaves the session unchanged.
    /// </summary>
    /// <param name="tenantId">The ID of the tenant to leave.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// HTTP 200 with <c>{ success: true, currentTenantId }</c> on success, where <c>currentTenantId</c>
    /// is null when the session no longer has a current tenant;
    /// HTTP 401 when unauthenticated;
    /// HTTP 403 when last-admin guard blocks;
    /// HTTP 404 when user is not a member.
    /// </returns>
    [HttpPost("tenants/{tenantId}/leave")]
    public async Task<IActionResult> LeaveTenant(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        // Only a GroundUp token identifies a GroundUp user; an identity-provider token's sub does not
        if (ResolveGroundUpUserId() is null)
        {
            return Unauthorized();
        }

        // auth_time is carried into the tenantless token so the absolute session lifetime is not restarted
        DateTimeOffset? originalAuthTime = null;
        var authTimeClaim = HttpContext.User.FindFirst("auth_time");
        if (authTimeClaim is not null && long.TryParse(authTimeClaim.Value, out var authTimeUnix))
        {
            originalAuthTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
        }

        var result = await _membershipService.LeaveTenantAsync(tenantId, originalAuthTime, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            return result.StatusCode switch
            {
                401 => Unauthorized(),
                404 => NotFound(new { error = result.Message }),
                403 => StatusCode(403, new { error = result.Message, errorCode = result.ErrorCode }),
                _ => StatusCode(result.StatusCode, new { error = result.Message })
            };
        }

        var outcome = result.Data;
        if (outcome.SessionEnded)
        {
            // The old tenant-scoped token must not stay in the cookie: replace it, or clear it
            if (outcome.SessionToken is not null)
            {
                await _authCookieWriter.WriteAuthCookieAsync(HttpContext, outcome.SessionToken);
            }
            else
            {
                await _authCookieWriter.ClearAuthCookieAsync(HttpContext);
            }
        }

        return Ok(new { success = true, currentTenantId = outcome.CurrentTenantId });
    }

    /// <summary>
    /// Resolves the GroundUp user ID from the current principal.
    /// A GroundUp token, with or without a current tenant, carries the user id as <c>sub</c>.
    /// An identity-provider principal's <c>sub</c> is the provider's id (also a GUID for Keycloak),
    /// so it is rejected here rather than guessed at.
    /// </summary>
    /// <returns>The user ID if resolvable from a GroundUp token, or null.</returns>
    private Guid? ResolveGroundUpUserId()
    {
        if (HttpContext.User.IsExternalIdp())
        {
            return null;
        }

        var userIdClaim = HttpContext.User.FindFirst(_authOptions.Value.UserIdClaimType)?.Value;
        return !string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId)
            ? userId
            : null;
    }
}
