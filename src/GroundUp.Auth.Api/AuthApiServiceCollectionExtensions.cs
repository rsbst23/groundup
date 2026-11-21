using GroundUp.Auth.Services.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Api;

/// <summary>
/// Registers the HTTP-only parts of the auth module. Call together with
/// <c>AddGroundUpAuth()</c> when the auth endpoints and middleware are used
/// (<see cref="AuthApplicationBuilderExtensions.UseGroundUpAuth"/>).
/// </summary>
public static class AuthApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core antiforgery for CSRF protection of cookie-authenticated requests:
    /// <list type="bullet">
    ///   <item>Request header <see cref="AuthConstants.CsrfHeaderName"/> (<c>X-CSRF-Token</c>).</item>
    ///   <item>Cookie <see cref="AuthConstants.CsrfCookieName"/>: HttpOnly, <c>SameSite=Strict</c>,
    ///   path <c>/</c>, host-only, Secure following <see cref="AuthOptions.CookieSecure"/> (the one
    ///   Secure setting every auth cookie uses).</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGroundUpAuthApi(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = AuthConstants.CsrfHeaderName;
            options.Cookie.Name = AuthConstants.CsrfCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";

            // This is an API: framing policy belongs to the host, not to the antiforgery service.
            options.SuppressXFrameOptionsHeader = true;
        });

        // Secure follows AuthOptions.CookieSecure so no cookie bypasses the one setting.
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IOptions<AuthOptions>>((options, authOptions) =>
            {
                options.Cookie.SecurePolicy = authOptions.Value.CookieSecure
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.None;
            });

        return services;
    }
}
