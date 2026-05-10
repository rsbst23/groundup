using System.Security.Claims;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Identity;

/// <summary>
/// JWT-based implementation of <see cref="ITenantContext"/> that extracts
/// the tenant identifier from claims in the current HTTP context.
/// Returns <see cref="Guid.Empty"/> when no HTTP context or tenant claim is present.
/// </summary>
public sealed class JwtTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<AuthOptions> _options;

    /// <summary>
    /// Initializes a new instance of <see cref="JwtTenantContext"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    /// <param name="options">Auth configuration options containing claim type mappings.</param>
    public JwtTenantContext(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    /// <inheritdoc />
    public Guid TenantId => ParseGuidClaim(_options.Value.TenantIdClaimType);

    private ClaimsPrincipal? GetUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user;
    }

    private Guid ParseGuidClaim(string claimType)
    {
        var value = GetUser()?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }
}
