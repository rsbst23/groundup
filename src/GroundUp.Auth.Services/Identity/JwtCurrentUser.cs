using System.Security.Claims;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace GroundUp.Auth.Services.Identity;

/// <summary>
/// JWT-based implementation of <see cref="ICurrentUser"/> that extracts
/// user identity from claims in the current HTTP context.
/// Returns <see cref="Guid.Empty"/> for UserId and null for string properties
/// when no authenticated user is present.
/// </summary>
public sealed class JwtCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<AuthOptions> _options;

    /// <summary>
    /// Initializes a new instance of <see cref="JwtCurrentUser"/>.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    /// <param name="options">Auth configuration options containing claim type mappings.</param>
    public JwtCurrentUser(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    /// <inheritdoc />
    public Guid UserId => ParseGuidClaim(_options.Value.UserIdClaimType);

    /// <inheritdoc />
    public string? Email => GetClaim(_options.Value.EmailClaimType);

    /// <inheritdoc />
    public string? DisplayName => GetClaim(_options.Value.DisplayNameClaimType);

    private ClaimsPrincipal? GetUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user;
    }

    private string? GetClaim(string claimType)
    {
        return GetUser()?.FindFirst(claimType)?.Value;
    }

    private Guid ParseGuidClaim(string claimType)
    {
        var value = GetClaim(claimType);
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }
}
