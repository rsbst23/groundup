using Microsoft.AspNetCore.Http;

namespace GroundUp.Auth.Services;

/// <summary>
/// Abstraction for writing and clearing the authentication cookie.
/// Implementations MUST honour <see cref="Configuration.AuthOptions.CookieName"/>,
/// <see cref="Configuration.AuthOptions.CookieSecure"/>, and
/// <see cref="Configuration.AuthOptions.CookieSameSite"/>.
/// <para>
/// Implementations MUST derive the cookie's Domain attribute from the
/// <c>auth.application.default-domain</c> system setting:
/// <list type="bullet">
///   <item>If empty → host-only cookie (no Domain attribute set)</item>
///   <item>If non-empty (e.g., "sampleapp.com") → Domain = ".sampleapp.com"</item>
/// </list>
/// </para>
/// <para>
/// Cookie expiration MUST align with <see cref="Configuration.AuthOptions.TokenExpirationMinutes"/>.
/// Phase 10C implements; 10A defines the contract only.
/// </para>
/// </summary>
public interface IAuthCookieWriter
{
    /// <summary>
    /// Writes the authentication cookie to the response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="token">The JWT token value to store in the cookie.</param>
    void WriteAuthCookie(HttpContext httpContext, string token);

    /// <summary>
    /// Clears the authentication cookie from the response.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    void ClearAuthCookie(HttpContext httpContext);
}
