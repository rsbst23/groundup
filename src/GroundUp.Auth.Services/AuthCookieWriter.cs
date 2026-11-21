using System.IdentityModel.Tokens.Jwt;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services;

/// <summary>
/// Concrete implementation of <see cref="IAuthCookieWriter"/> that writes and clears
/// the authentication cookie. The Domain attribute is derived from the
/// <c>auth.application.default-domain</c> setting via <see cref="ISettingsService"/>.
/// <para>
/// The auth cookie expires when the token inside it expires (the token's <c>exp</c> claim),
/// so the cookie follows the effective session policy without a second lookup. The id_token
/// cookie expires with the auth cookie written earlier in the same request.
/// </para>
/// </summary>
public sealed class AuthCookieWriter : IAuthCookieWriter
{
    private const string DefaultDomainSettingKey = "auth.application.default-domain";
    private const string AuthCookieExpiryItemKey = "GroundUp.AuthCookieExpiry";

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
    public Task WriteAuthCookieAsync(HttpContext httpContext, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must not be null, empty, or whitespace.", nameof(token));
        }

        return WriteAuthCookieCoreAsync(httpContext, token);
    }

    private async Task WriteAuthCookieCoreAsync(HttpContext httpContext, string token)
    {
        var authOptions = _options.Value;
        var cookieOptions = await BuildCookieOptionsAsync(authOptions);

        // The cookie lives exactly as long as the token. A value that is not a readable JWT
        // (never the case for tokens issued by TokenService) falls back to the configured lifetime.
        var expires = ReadTokenExpiry(token) ?? DateTimeOffset.UtcNow.AddMinutes(authOptions.TokenExpirationMinutes);
        cookieOptions.Expires = expires;
        httpContext.Items[AuthCookieExpiryItemKey] = expires;

        httpContext.Response.Cookies.Append(authOptions.CookieName, token, cookieOptions);
    }

    /// <inheritdoc />
    public async Task WriteIdTokenCookieAsync(HttpContext httpContext, string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return; // Best-effort — if no id_token, skip silently
        }

        var authOptions = _options.Value;
        var cookieOptions = await BuildCookieOptionsAsync(authOptions);

        // id_token cookie expires with the auth cookie written in this request.
        cookieOptions.Expires = httpContext.Items.TryGetValue(AuthCookieExpiryItemKey, out var written)
            && written is DateTimeOffset authExpiry
                ? authExpiry
                : DateTimeOffset.UtcNow.AddMinutes(authOptions.TokenExpirationMinutes);

        httpContext.Response.Cookies.Append(authOptions.IdTokenCookieName, idToken, cookieOptions);
    }

    /// <inheritdoc />
    public async Task ClearIdTokenCookieAsync(HttpContext httpContext)
    {
        var authOptions = _options.Value;
        var cookieOptions = await BuildCookieOptionsAsync(authOptions);
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(-1);
        httpContext.Response.Cookies.Append(authOptions.IdTokenCookieName, string.Empty, cookieOptions);
    }

    /// <inheritdoc />
    public async Task ClearAuthCookieAsync(HttpContext httpContext)
    {
        var authOptions = _options.Value;
        var cookieOptions = await BuildCookieOptionsAsync(authOptions);

        // Set expiration in the past to delete the cookie.
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(-1);

        httpContext.Response.Cookies.Append(authOptions.CookieName, string.Empty, cookieOptions);
    }

    private async Task<CookieOptions> BuildCookieOptionsAsync(AuthOptions authOptions)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = authOptions.CookieSecure,
            SameSite = authOptions.CookieSameSite,
            Path = "/"
        };

        var domain = await GetDomainAsync();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            cookieOptions.Domain = $".{domain.Trim()}";
        }

        return cookieOptions;
    }

    private async Task<string?> GetDomainAsync()
    {
        // Use the convenience overload that resolves the scope chain internally.
        var result = await _settingsService.GetAsync<string>(DefaultDomainSettingKey);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Data))
        {
            return null;
        }

        return result.Data;
    }

    private static DateTimeOffset? ReadTokenExpiry(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return null;
        }

        var validTo = handler.ReadJwtToken(token).ValidTo;
        return validTo == DateTime.MinValue
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(validTo, DateTimeKind.Utc));
    }
}
