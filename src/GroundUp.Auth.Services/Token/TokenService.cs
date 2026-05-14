using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Configuration;
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
    private readonly AuthOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenService"/> class.
    /// </summary>
    /// <param name="userRepository">Repository for resolving user details.</param>
    /// <param name="userRoleRepository">Repository for resolving user role assignments.</param>
    /// <param name="signingKeyProvider">Provider for resolving signing keys.</param>
    /// <param name="options">Auth configuration options.</param>
    public TokenService(
        IUserRepository userRepository,
        IUserRoleRepository userRoleRepository,
        ISigningKeyProvider signingKeyProvider,
        IOptions<AuthOptions> options)
    {
        _userRepository = userRepository;
        _userRoleRepository = userRoleRepository;
        _signingKeyProvider = signingKeyProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string?> GenerateTokenAsync(Guid userId, Guid tenantId, IEnumerable<Claim>? additionalClaims = null)
    {
        // 1. Resolve user — return null if not found
        var userResult = await _userRepository.GetByIdAsync(userId);
        if (!userResult.Success || userResult.Data is null)
        {
            return null;
        }

        var user = userResult.Data;

        // 2. Resolve tenant-scoped roles
        var tenantRolesResult = await _userRoleRepository.GetByUserIdAsync(userId);
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

        // 5. Get signing key
        var signingKeyInfo = await _signingKeyProvider.GetSigningKeyAsync(tenantId);

        // 6. Build claims
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

        // 7. Add additional claims
        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        // 8. Create security key and signing credentials
        var securityKey = new SymmetricSecurityKey(signingKeyInfo.KeyMaterial);
        var signingCredentials = new SigningCredentials(securityKey, MapAlgorithm(signingKeyInfo.Algorithm));

        // 9. Create token descriptor
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_options.TokenExpirationMinutes),
            SigningCredentials = signingCredentials
        };

        // 10. Create and write token with kid header
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        token.Header["kid"] = signingKeyInfo.KeyId;

        return tokenHandler.WriteToken(token);
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
            var tokenHandler = new JwtSecurityTokenHandler();

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
