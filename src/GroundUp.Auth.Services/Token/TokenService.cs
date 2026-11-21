using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Session;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GroundUp.Auth.Services.Token;

/// <summary>
/// Generates and validates GroundUp-issued JWT tokens containing user identity,
/// tenant, and role claims. Uses <see cref="ISigningKeyProvider"/> for key resolution
/// and includes a <c>kid</c> header for key identification during validation.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly ISigningKeyProvider _signingKeyProvider;
    private readonly ISessionPolicyProvider _sessionPolicyProvider;
    private readonly AuthOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenService"/> class.
    /// </summary>
    /// <param name="userRepository">Repository for resolving user details.</param>
    /// <param name="userRoleRepository">Repository for resolving user role assignments.</param>
    /// <param name="signingKeyProvider">Provider for resolving signing keys.</param>
    /// <param name="sessionPolicyProvider">Resolves the effective token and absolute session lifetimes.</param>
    /// <param name="options">Auth configuration options (issuer and audience).</param>
    public TokenService(
        IUserRepository userRepository,
        IUserRoleRepository userRoleRepository,
        ISigningKeyProvider signingKeyProvider,
        ISessionPolicyProvider sessionPolicyProvider,
        IOptions<AuthOptions> options)
    {
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
        _signingKeyProvider = signingKeyProvider;
        _sessionPolicyProvider = sessionPolicyProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string?> GenerateTokenAsync(Guid userId, Guid tenantId, IEnumerable<Claim>? additionalClaims = null)
    {
        // 1. Resolve user — return null if not found or inactive
        var userResult = await _userRepository.GetByIdAsync(userId);
        if (!userResult.Success || userResult.Data is null)
        {
            return null;
        }

        var user = userResult.Data;
        if (!user.IsActive)
        {
            return null;
        }

        // 2. Resolve tenant-scoped roles (uses explicit tenantId — bypasses ambient context
        //    because at sign-in / cross-tenant refresh the ambient context may not match)
        var tenantRolesResult = await _userRoleRepository.GetByUserIdForTenantAsync(userId, tenantId);
        var tenantRoles = tenantRolesResult.Success && tenantRolesResult.Data is not null
            ? tenantRolesResult.Data
            : new List<UserRoleDto>();

        // 3. Resolve system roles
        var systemRolesResult = await _userRoleRepository.GetSystemRolesForUserAsync(userId);
        var systemRoles = systemRolesResult.Success && systemRolesResult.Data is not null
            ? systemRolesResult.Data
            : new List<UserRoleDto>();

        // 4. Combine and deduplicate role names
        var roleNames = tenantRoles
            .Concat(systemRoles)
            .Where(r => r.RoleName is not null)
            .Select(r => r.RoleName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 5. Build claims
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tid", tenantId.ToString()),
            new("email", user.Email),
            new("name", user.DisplayName)
        };

        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim("role", roleName));
        }

        // 6. Add additional claims
        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var policy = await _sessionPolicyProvider.GetPolicyForTenantAsync(tenantId);
        return await SignTokenAsync(claims, tenantId, policy);
    }

    /// <inheritdoc />
    public async Task<string?> GenerateTenantlessTokenAsync(Guid userId, IEnumerable<Claim>? additionalClaims = null)
    {
        var userResult = await _userRepository.GetByIdAsync(userId);
        if (!userResult.Success || userResult.Data is null || !userResult.Data.IsActive)
        {
            return null;
        }

        var user = userResult.Data;

        // Identity only: no tid and no roles (not even system roles).
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("email", user.Email),
            new("name", user.DisplayName)
        };

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        // No tenant yet: the System session policy applies.
        var policy = await _sessionPolicyProvider.GetSystemPolicyAsync();

        // Guid.Empty selects the system signing key (the token is not scoped to a tenant).
        return await SignTokenAsync(claims, Guid.Empty, policy);
    }

    /// <summary>
    /// Signs the claims with the key resolved for <paramref name="signingTenantId"/> and writes the token,
    /// including the <c>kid</c> header used to find the key again during validation.
    /// The expiry is the token lifetime from now, but never later than the absolute session
    /// lifetime counted from the <c>auth_time</c> claim, so a refreshed token cannot outlive the cap.
    /// </summary>
    private async Task<string> SignTokenAsync(List<Claim> claims, Guid signingTenantId, SessionPolicy policy)
    {
        var signingKeyInfo = await _signingKeyProvider.GetSigningKeyAsync(signingTenantId);

        var securityKey = new SymmetricSecurityKey(signingKeyInfo.KeyMaterial);
        var signingCredentials = new SigningCredentials(securityKey, MapAlgorithm(signingKeyInfo.Algorithm));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = ComputeExpiry(claims, policy),
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        token.Header["kid"] = signingKeyInfo.KeyId;

        return tokenHandler.WriteToken(token);
    }

    private static DateTime ComputeExpiry(List<Claim> claims, SessionPolicy policy)
    {
        var now = DateTime.UtcNow;
        var expiry = now.Add(policy.TokenLifetime);

        var authTimeClaim = claims.FirstOrDefault(c => c.Type == "auth_time");
        if (authTimeClaim is not null && long.TryParse(authTimeClaim.Value, out var authTimeUnix))
        {
            var cap = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix).UtcDateTime.Add(policy.AbsoluteLifetime);
            if (cap < expiry)
            {
                // Callers reject sessions already past the cap before asking for a token, so this
                // floor only guards against an expiry that is not after the token's issue time.
                expiry = cap > now ? cap : now.AddSeconds(1);
            }
        }

        return expiry;
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        // 1. If token is null/empty, return null
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            // Disable inbound claim mapping so that "sub", "tid", "email", "role"
            // remain as the original short claim types after validation.
            // Without this, JwtSecurityTokenHandler maps standard JWT claims to long
            // URI forms (e.g. "sub" → ClaimTypes.NameIdentifier), which breaks
            // downstream consumers that look up claims by their JWT names.
            var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };

            // 2. Read the JWT header to extract kid (without full validation)
            var jwtToken = tokenHandler.ReadJwtToken(token);
            var kid = jwtToken.Header.Kid;

            if (string.IsNullOrEmpty(kid))
            {
                return null;
            }

            // 3. Get validation key from ISigningKeyProvider
            var keyInfo = await _signingKeyProvider.GetValidationKeyAsync(kid);
            if (keyInfo is null)
            {
                return null;
            }

            // 4. Build validation parameters
            var securityKey = new SymmetricSecurityKey(keyInfo.KeyMaterial);
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = _options.Issuer,
                ValidAudience = _options.Audience,
                IssuerSigningKey = securityKey,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            // 5. Validate token
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            // 6. Never throw — return null on any failure
            return null;
        }
    }

    /// <summary>
    /// Maps the algorithm string from SigningKeyInfo to the corresponding SecurityAlgorithms constant.
    /// </summary>
    private static string MapAlgorithm(string algorithm) => algorithm.ToUpperInvariant() switch
    {
        "HS256" => SecurityAlgorithms.HmacSha256Signature,
        "HS384" => SecurityAlgorithms.HmacSha384Signature,
        "HS512" => SecurityAlgorithms.HmacSha512Signature,
        _ => SecurityAlgorithms.HmacSha256Signature
    };
}
