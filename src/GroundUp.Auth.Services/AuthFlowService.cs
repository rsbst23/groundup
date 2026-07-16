using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Orchestrator service for authentication flows. Creates <c>AuthFlowState</c> rows
/// at initiation and dispatches OAuth callbacks to the matching <see cref="IFlowHandler"/>
/// based on the stored <c>FlowType</c>.
/// </summary>
public sealed class AuthFlowService : IAuthFlowService
{
    private readonly IAuthUrlBuilder _authUrlBuilder;
    private readonly IAuthFlowStateService _authFlowStateService;
    private readonly IAuthFlowStateRepository _authFlowStateRepository;
    private readonly IEnumerable<IFlowHandler> _flowHandlers;
    private readonly HostResolvedTenant _hostResolvedTenant;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly ILogger<AuthFlowService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthFlowService"/>.
    /// </summary>
    /// <param name="authUrlBuilder">Builds Keycloak authorization URLs with security parameters.</param>
    /// <param name="authFlowStateService">Manages AuthFlowState lifecycle (initiate, consume, mark failed).</param>
    /// <param name="authFlowStateRepository">Repository for AuthFlowState lookup by state token.</param>
    /// <param name="flowHandlers">All registered flow handler implementations.</param>
    /// <param name="hostResolvedTenant">The host-resolved tenant for the current request.</param>
    /// <param name="authOptions">Authentication configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public AuthFlowService(
        IAuthUrlBuilder authUrlBuilder,
        IAuthFlowStateService authFlowStateService,
        IAuthFlowStateRepository authFlowStateRepository,
        IEnumerable<IFlowHandler> flowHandlers,
        HostResolvedTenant hostResolvedTenant,
        IOptions<AuthOptions> authOptions,
        ILogger<AuthFlowService> logger)
    {
        _authUrlBuilder = authUrlBuilder;
        _authFlowStateService = authFlowStateService;
        _authFlowStateRepository = authFlowStateRepository;
        _flowHandlers = flowHandlers;
        _hostResolvedTenant = hostResolvedTenant;
        _authOptions = authOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<FlowInitiationResult>> InitiateFlowAsync(
        FlowInitiationRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var options = _authOptions.Value;
        var tenant = _hostResolvedTenant.Tenant;

        // Determine realm override for enterprise tenants
        var realmOverride = tenant?.RealmName;

        // Build the absolute redirect URI from the request scheme/host + configured callback path
        var redirectUri = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{options.CallbackPath}";

        // Build the authorization URL (generates state, nonce, PKCE)
        var urlRequest = new AuthUrlRequest(realmOverride, redirectUri);
        var urlResult = await _authUrlBuilder.BuildAuthorizationUrlAsync(urlRequest, cancellationToken);

        if (!urlResult.Success)
        {
            return OperationResult<FlowInitiationResult>.Fail(
                urlResult.Message,
                urlResult.StatusCode,
                urlResult.ErrorCode);
        }

        var authUrl = urlResult.Data!;

        // Extract client IP and user agent from HttpContext
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
        if (string.IsNullOrEmpty(userAgent))
        {
            userAgent = null;
        }

        // Create the AuthFlowState via the service
        var initiateRequest = new InitiateAuthFlowRequest(
            FlowType: request.FlowType,
            TenantId: tenant?.Id,
            InvitationId: null,
            JoinLinkId: null,
            Realm: realmOverride,
            ReturnUrl: request.ReturnUrl,
            StateToken: authUrl.StateToken,
            CodeVerifier: authUrl.CodeVerifier,
            RedirectUri: authUrl.RedirectUri,
            OrganizationName: request.FlowType == FlowType.NewOrganization ? request.OrganizationName : null,
            Nonce: authUrl.Nonce,
            CreatedByIp: clientIp,
            CreatedByUserAgent: userAgent,
            Lifetime: TimeSpan.FromMinutes(options.FlowStateExpirationMinutes));

        var stateResult = await _authFlowStateService.InitiateAsync(initiateRequest, cancellationToken);

        if (!stateResult.Success)
        {
            return OperationResult<FlowInitiationResult>.Fail(
                stateResult.Message,
                stateResult.StatusCode,
                stateResult.ErrorCode);
        }

        var flowState = stateResult.Data!;

        // Set the state cookie for browser-binding CSRF protection
        SetStateCookie(httpContext, authUrl.StateToken, options);

        _logger.LogDebug(
            "Auth flow initiated: FlowType={FlowType}, StateId={FlowStateId}",
            request.FlowType,
            flowState.Id);

        return OperationResult<FlowInitiationResult>.Ok(
            new FlowInitiationResult(authUrl.AuthorizationUrl, flowState.Id));
    }

    /// <inheritdoc />
    public async Task<OperationResult<FlowResult>> HandleCallbackAsync(
        string code,
        string state,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var options = _authOptions.Value;

        // 1. Read and validate the state cookie binding (CSRF protection)
        var stateCookie = httpContext.Request.Cookies[options.StateCookieName];

        if (string.IsNullOrEmpty(stateCookie))
        {
            _logger.LogWarning("Callback rejected: state cookie '{CookieName}' is missing", options.StateCookieName);
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("CSRF_STATE_MISMATCH", "State cookie is missing — possible CSRF or session expired.", 403));
        }

        if (!string.Equals(state, stateCookie, StringComparison.Ordinal))
        {
            _logger.LogWarning("Callback rejected: state parameter does not match state cookie");
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("CSRF_STATE_MISMATCH", "State parameter does not match state cookie — possible CSRF.", 403));
        }

        // 2. Look up the AuthFlowState by StateToken
        var lookupResult = await _authFlowStateRepository.FindByStateTokenAsync(state, cancellationToken);

        if (!lookupResult.Success)
        {
            _logger.LogWarning("Callback rejected: no AuthFlowState found for state token");
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("STATE_NOT_FOUND", "No matching flow state found.", 404));
        }

        var flowState = lookupResult.Data!;

        // 3. Check if already consumed (replay attack)
        if (flowState.Status == FlowStatus.Consumed)
        {
            _logger.LogWarning(
                "Callback rejected: AuthFlowState '{FlowStateId}' already consumed (replay attack)",
                flowState.Id);
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("FLOW_ALREADY_CONSUMED", "This flow has already been processed.", 410));
        }

        // 4. Check if expired
        if (flowState.Status == FlowStatus.Expired || flowState.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning(
                "Callback rejected: AuthFlowState '{FlowStateId}' has expired",
                flowState.Id);
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("FLOW_EXPIRED", "The authentication flow has expired. Please try again.", 400));
        }

        // 5. Check if already failed
        if (flowState.Status == FlowStatus.Failed)
        {
            _logger.LogWarning(
                "Callback rejected: AuthFlowState '{FlowStateId}' is in failed state",
                flowState.Id);
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("FLOW_FAILED", "This flow has already failed.", 400));
        }

        // 6. Consume the state (transition Pending → Consumed)
        var consumeResult = await _authFlowStateService.ConsumeAsync(
            flowState.Id, flowState.FlowType, cancellationToken);

        if (!consumeResult.Success)
        {
            // Could be a race condition where another request consumed it first
            if (consumeResult.StatusCode == 409)
            {
                return OperationResult<FlowResult>.Ok(
                    FlowResult.Error("FLOW_ALREADY_CONSUMED", "This flow has already been processed.", 410));
            }

            _logger.LogWarning(
                "Callback rejected: failed to consume AuthFlowState '{FlowStateId}': {Message}",
                flowState.Id,
                consumeResult.Message);
            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("CONSUME_FAILED", consumeResult.Message, consumeResult.StatusCode));
        }

        var consumedState = consumeResult.Data!;

        // 7. Clear the state cookie (single-use)
        ClearStateCookie(httpContext, options);

        // 8. Resolve the correct flow handler
        var handler = _flowHandlers.FirstOrDefault(
            h => h.HandledFlowType == consumedState.FlowType);

        if (handler is null)
        {
            _logger.LogError(
                "No flow handler registered for FlowType '{FlowType}'",
                consumedState.FlowType);

            // Mark the flow as failed since no handler can process it
            await _authFlowStateService.MarkFailedAsync(
                consumedState.Id,
                $"No handler registered for FlowType '{consumedState.FlowType}'",
                cancellationToken);

            return OperationResult<FlowResult>.Ok(
                FlowResult.Error("NO_HANDLER", $"No handler registered for flow type '{consumedState.FlowType}'.", 500));
        }

        // 9. Build callback context and invoke the handler
        var callbackContext = new FlowCallbackContext(
            AuthorizationCode: code,
            CodeVerifier: consumedState.CodeVerifier,
            RedirectUri: consumedState.RedirectUri,
            ConsumedState: consumedState,
            HostResolvedTenant: _hostResolvedTenant.Tenant);

        _logger.LogDebug(
            "Dispatching callback to handler for FlowType={FlowType}, StateId={FlowStateId}",
            consumedState.FlowType,
            consumedState.Id);

        var flowResult = await handler.HandleCallbackAsync(callbackContext);

        return OperationResult<FlowResult>.Ok(flowResult);
    }

    /// <summary>
    /// Sets the state cookie for browser-binding CSRF protection.
    /// Short-lived, HttpOnly, Secure, SameSite=Strict, Path="/".
    /// </summary>
    private static void SetStateCookie(HttpContext httpContext, string stateToken, AuthOptions options)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            // State cookie lives as long as the flow state
            Expires = DateTimeOffset.UtcNow.AddMinutes(options.FlowStateExpirationMinutes)
        };

        httpContext.Response.Cookies.Append(options.StateCookieName, stateToken, cookieOptions);
    }

    /// <summary>
    /// Clears the state cookie after successful consumption.
    /// </summary>
    private static void ClearStateCookie(HttpContext httpContext, AuthOptions options)
    {
        httpContext.Response.Cookies.Delete(options.StateCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }
}
