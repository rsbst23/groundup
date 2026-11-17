using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Sample.Tests.Integration.Fixtures;

/// <summary>
/// Mints tenant-scoped GroundUp JWTs for tests, signed with the key that
/// <c>GroundUpWebApplicationFactory</c> configures (<c>GroundUp:Auth:JwtSigningKey</c>).
/// The framework takes the current tenant from the token's <c>tid</c> claim; there is no tenant header.
/// </summary>
internal static class TestTokens
{
    // Must match GroundUpWebApplicationFactory.ConfigureWebHost.
    private const string SigningKey = "integration-test-signing-key-that-is-at-least-32-bytes-long!";

    /// <summary>
    /// Creates a signed token for <paramref name="tenantId"/>. Pass <c>null</c> to create a token
    /// with no <c>tid</c> claim, which represents a session with no current tenant.
    /// </summary>
    public static string Create(Guid? tenantId, Guid? userId = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", (userId ?? Guid.NewGuid()).ToString()),
            new("email", "test@example.com"),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tid", tenantId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: "GroundUp",
            audience: "GroundUp",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: credentials);

        // TokenService rejects tokens without a kid header; "default" is the configuration-backed key id.
        token.Header["kid"] = "default";

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
