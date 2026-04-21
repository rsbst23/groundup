using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Common.Fixtures;

/// <summary>
/// Authentication handler that auto-authenticates all requests with a default test user.
/// Registered in <see cref="GroundUpWebApplicationFactory{TEntryPoint, TContext}"/> but not
/// actively enforced until <c>[Authorize]</c> attributes are added in Phase 9.
/// <para>
/// The default test user identity contains the following claims:
/// <list type="bullet">
///   <item><description><see cref="ClaimTypes.NameIdentifier"/>: <c>test-user-id</c></description></item>
///   <item><description><see cref="ClaimTypes.Name"/>: <c>Test User</c></description></item>
///   <item><description><see cref="ClaimTypes.Email"/>: <c>test@example.com</c></description></item>
/// </list>
/// </para>
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The authentication scheme name used when registering this handler.
    /// </summary>
    public const string SchemeName = "TestScheme";

    /// <summary>
    /// Initializes a new instance of <see cref="TestAuthHandler"/>.
    /// </summary>
    /// <param name="options">The authentication scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="clock">The system clock.</param>
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    /// <summary>
    /// Authenticates the request by returning a successful result with a default test user principal.
    /// </summary>
    /// <returns>An <see cref="AuthenticateResult"/> containing the test user identity.</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "test@example.com")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
