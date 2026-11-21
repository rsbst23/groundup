using System.Security.Claims;

namespace GroundUp.Auth.Api;

/// <summary>
/// Constants shared by the auth middleware and controller.
/// </summary>
public static class AuthConstants
{
    /// <summary>
    /// Authentication type of the principal built from an identity-provider token
    /// (for example a Keycloak access token sent as a Bearer header). Its <c>sub</c> is the
    /// provider's user id, not a GroundUp user id. GroundUp-issued tokens use a different
    /// authentication type.
    /// </summary>
    public const string ExternalIdpAuthenticationType = "ExternalIdP";

    /// <summary>
    /// Request header that carries the CSRF request token on cookie-authenticated
    /// state-changing requests. The token comes from <c>GET /auth/csrf</c>.
    /// </summary>
    public const string CsrfHeaderName = "X-CSRF-Token";

    /// <summary>
    /// Name of the HttpOnly cookie holding the antiforgery cookie token that the request token
    /// is checked against.
    /// </summary>
    public const string CsrfCookieName = "CsrfToken";

    /// <summary>Error code returned with HTTP 403 when the CSRF token is missing or invalid.</summary>
    public const string CsrfValidationFailedErrorCode = "CSRF_VALIDATION_FAILED";

    /// <summary>Error code returned with HTTP 500 when antiforgery services are not registered.</summary>
    public const string CsrfNotConfiguredErrorCode = "CSRF_NOT_CONFIGURED";

    /// <summary>
    /// True when the principal was built from an identity-provider token rather than a GroundUp token.
    /// The absence of a <c>tid</c> claim does not identify such a principal: a GroundUp token
    /// without <c>tid</c> is a tenantless session, and its <c>sub</c> is the GroundUp user id.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    public static bool IsExternalIdp(this ClaimsPrincipal principal) =>
        principal.Identities.Any(i => i.IsAuthenticated
            && string.Equals(i.AuthenticationType, ExternalIdpAuthenticationType, StringComparison.Ordinal));
}
