using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GroundUp.Core.Abstractions;

namespace GroundUp.Api.Authentication;

/// <summary>
/// Authentication handler for the bootstrap admin token.
/// Validates Bearer token against GroundUp:BootstrapAdminToken configuration.
/// Automatically rejects all requests once setup is complete.
/// Uses constant-time comparison to prevent timing attacks.
/// </summary>
public sealed class BootstrapAdminTokenAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The authentication scheme name used for bootstrap admin token authentication.
    /// </summary>
    public const string SchemeName = "BootstrapAdminToken";

    /// <summary>
    /// The claim type added to the identity upon successful authentication.
    /// </summary>
    public const string ClaimType = "bootstrap-admin";

    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of <see cref="BootstrapAdminTokenAuthenticationHandler"/>.
    /// </summary>
    public BootstrapAdminTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var bootstrap = Context.RequestServices.GetRequiredService<IBootstrapStateService>();

        if (await bootstrap.IsCompleteAsync(Context.RequestAborted))
            return AuthenticateResult.Fail("Setup is already complete; bootstrap token is no longer accepted.");

        var authorization = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Missing Authorization header.");

        var provided = authorization["Bearer ".Length..];

        var configured = _configuration["GroundUp:BootstrapAdminToken"];

        if (string.IsNullOrEmpty(configured))
            return AuthenticateResult.Fail("Bootstrap admin token is not configured.");

        var providedBytes = Encoding.UTF8.GetBytes(provided.Trim());
        var configuredBytes = Encoding.UTF8.GetBytes(configured);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
            return AuthenticateResult.Fail("Invalid bootstrap admin token.");

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimType, "true") },
            SchemeName);

        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
