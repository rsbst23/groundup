using GroundUp.Core;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Session;

/// <summary>
/// Used by the login callback handlers to apply the absolute session lifetime to a new sign-in.
/// <list type="bullet">
///   <item><see cref="ResolveAuthTime"/>: the session is counted from the identity provider's
///   <c>auth_time</c> (when the user really authenticated), so a silent single sign-on does not
///   restart the clock. Falls back to now, with a warning, if the provider sent no <c>auth_time</c>.</item>
///   <item><see cref="CheckAsync"/>: refuses to issue a session that is already past the lifetime of
///   the System level or of the tenant being entered, with <c>401 REAUTH_REQUIRED</c>. The client
///   then starts a login with <c>forceLogin=true</c>, which makes the identity provider ask for
///   credentials.</item>
/// </list>
/// </summary>
public sealed class CallbackSessionGuard
{
    private readonly ISessionPolicyProvider _sessionPolicyProvider;
    private readonly ILogger<CallbackSessionGuard> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CallbackSessionGuard"/>.
    /// </summary>
    /// <param name="sessionPolicyProvider">Resolves the absolute session lifetime.</param>
    /// <param name="logger">Logger.</param>
    public CallbackSessionGuard(ISessionPolicyProvider sessionPolicyProvider, ILogger<CallbackSessionGuard> logger)
    {
        _sessionPolicyProvider = sessionPolicyProvider;
        _logger = logger;
    }

    /// <summary>
    /// Returns the time the session is counted from.
    /// </summary>
    /// <param name="idToken">The validated ID token.</param>
    public DateTimeOffset ResolveAuthTime(ValidatedIdToken idToken)
    {
        if (idToken.AuthTime is { } authTime)
        {
            // A value in the future (clock skew between servers) must not extend the session.
            var now = DateTimeOffset.UtcNow;
            return authTime > now ? now : authTime;
        }

        _logger.LogWarning(
            "The identity provider's ID token has no auth_time claim; counting the session from now. " +
            "The absolute session lifetime cannot detect a silent single sign-on in this case.");
        return DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns a <c>401 REAUTH_REQUIRED</c> result when a session that started at
    /// <paramref name="authTime"/> has reached the absolute lifetime, otherwise null.
    /// </summary>
    /// <param name="authTime">When the user last authenticated.</param>
    /// <param name="tenantId">The tenant being entered, or null for a tenantless session (System lifetime).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FlowResult?> CheckAsync(DateTimeOffset authTime, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var policy = tenantId is { } id
            ? await _sessionPolicyProvider.GetPolicyForTenantAsync(id, cancellationToken)
            : await _sessionPolicyProvider.GetSystemPolicyAsync(cancellationToken);

        if (DateTimeOffset.UtcNow - authTime < policy.AbsoluteLifetime)
        {
            return null;
        }

        _logger.LogInformation(
            "Sign-in refused: the identity provider session is older than the absolute session lifetime ({Minutes} min) for tenant {TenantId}",
            (int)policy.AbsoluteLifetime.TotalMinutes,
            tenantId);

        return FlowResult.Error(
            ErrorCodes.ReauthRequired,
            "Your session has reached its maximum lifetime. Please sign in again.",
            httpStatus: 401);
    }
}
