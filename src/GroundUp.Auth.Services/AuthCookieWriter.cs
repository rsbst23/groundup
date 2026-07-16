using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Concrete implementation of <see cref="IAuthCookieWriter"/> that writes and clears
/// the authentication cookie. The Domain attribute is derived from the
/// <c>auth.application.default-domain</c> setting via <see cref="ISettingsService"/>.
/// </summary>
public sealed class AuthCookieWriter : IAuthCookieWriter
{
    private const string DefaultDomainSettingKey = "auth.application.default-domain";

    private readonly ISettingsService _settingsService;
    private readonly IOptions<AuthOptions> _options;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthCookieWriter"/>.
    /// </summary>
    /// <param name="settingsService">The settings resolution service.</param>
    /// <param name="options">Auth configuration options.</param>
    public AuthCookieWriter(
        ISettingsService settingsService,
        IOptions<AuthOptions> options)
    {
        _settingsService = settingsService;
        _options = options;
    }

    /// <inheritdoc />
    public void WriteAuthCookie(HttpContext httpContext, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must not be null, empty, or whitespace.", nameof(token));
        }

        var authOptions = _options.Value;
        var cookieOptions = BuildCookieOptions(authOptions);

        // Set expiration to TokenExpirationMinutes from now (persistent cookie).
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddMinutes(authOptions.TokenExpirationMinutes);

        httpContext.Response.Cookies.Append(authOptions.CookieName, token, cookieOptions);
    }

    /// <inheritdoc />
    public void ClearAuthCookie(HttpContext httpContext)
    {
        var authOptions = _options.Value;
        var cookieOptions = BuildCookieOptions(authOptions);

        // Set expiration in the past to delete the cookie.
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(-1);

        httpContext.Response.Cookies.Append(authOptions.CookieName, string.Empty, cookieOptions);
    }

    private CookieOptions BuildCookieOptions(AuthOptions authOptions)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = authOptions.CookieSecure,
            SameSite = authOptions.CookieSameSite,
            Path = "/"
        };

        var domain = GetDomain();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            cookieOptions.Domain = $".{domain.Trim()}";
        }

        return cookieOptions;
    }

    private string? GetDomain()
    {
        // Use the convenience overload that resolves the scope chain internally.
        var result = _settingsService.GetAsync<string>(DefaultDomainSettingKey).GetAwaiter().GetResult();
        if (!result.Success || string.IsNullOrWhiteSpace(result.Data))
        {
            return null;
        }

        return result.Data;
    }
}
