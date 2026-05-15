using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Token;
using GroundUp.Core.Results;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services.Token;

public sealed class TokenServiceTests
{
    private const string SigningKey = "ThisIsAValidSigningKeyThatIs32BytesLong!!";

    private readonly IUserRepository _userRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly ISigningKeyProvider _signingKeyProvider;
    private readonly TokenService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    public TokenServiceTests()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _userRoleRepository = Substitute.For<IUserRoleRepository>();
        _signingKeyProvider = Substitute.For<ISigningKeyProvider>();

        var options = Options.Create(new AuthOptions
        {
            JwtSigningKey = SigningKey,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            TokenExpirationMinutes = 60
        });

        _sut = new TokenService(_userRepository, _userRoleRepository, _signingKeyProvider, options);

        // Default signing key setup
        var keyBytes = Encoding.UTF8.GetBytes(SigningKey);
        var keyInfo = new SigningKeyInfo("default", keyBytes, "HS256");
        _signingKeyProvider.GetSigningKeyAsync(Arg.Any<Guid>()).Returns(keyInfo);
        _signingKeyProvider.GetValidationKeyAsync("default").Returns(keyInfo);
    }

    // --- GenerateTokenAsync tests ---

    [Fact]
    public async Task GenerateTokenAsync_ValidUser_ReturnsToken()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task GenerateTokenAsync_UserNotFound_ReturnsNull()
    {
        // Arrange
        _userRepository.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.NotFound("User not found"));

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsSubClaim()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(_userId.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsTidClaim()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(_tenantId.ToString(), jwt.Claims.First(c => c.Type == "tid").Value);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsEmailClaim()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("user@test.com", jwt.Claims.First(c => c.Type == "email").Value);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsNameClaim()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("Test User", jwt.Claims.First(c => c.Type == "name").Value);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsKidHeader()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("default", jwt.Header.Kid);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsIssuer()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("TestIssuer", jwt.Issuer);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsAudience()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains("TestAudience", jwt.Audiences);
    }

    [Fact]
    public async Task GenerateTokenAsync_RolesAggregatedFromTenantAndSystem()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles(("Editor", Guid.NewGuid()), ("Viewer", Guid.NewGuid()));
        SetupSystemRoles(("Admin", Guid.NewGuid()));

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
        Assert.Contains("Editor", roles);
        Assert.Contains("Viewer", roles);
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task GenerateTokenAsync_DuplicateRolesDeduped()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        var roleId = Guid.NewGuid();
        SetupTenantRoles(("Admin", roleId));
        SetupSystemRoles(("Admin", roleId)); // Same role name in both

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
        Assert.Single(roles, r => r == "Admin");
    }

    [Fact]
    public async Task GenerateTokenAsync_AdditionalClaimsIncluded()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var additionalClaims = new[] { new Claim("custom_field", "custom_value") };

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId, additionalClaims);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("custom_value", jwt.Claims.First(c => c.Type == "custom_field").Value);
    }

    // --- ValidateTokenAsync tests ---

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsClaimsPrincipal()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Act
        var principal = await _sut.ValidateTokenAsync(token!);

        // Assert
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_RoundTrip_ClaimsMatch()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles(("Editor", Guid.NewGuid()));
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Act
        var principal = await _sut.ValidateTokenAsync(token!);

        // Assert — claims preserve their JWT-native names because TokenService disables
        // JwtSecurityTokenHandler's inbound claim type mapping.
        Assert.NotNull(principal);
        Assert.Equal(_userId.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal(_tenantId.ToString(), principal.FindFirst("tid")?.Value);
        Assert.Equal("user@test.com", principal.FindFirst("email")?.Value);
        Assert.Equal("Test User", principal.FindFirst("name")?.Value);
        Assert.Equal("Editor", principal.FindFirst("role")?.Value);
    }

    [Fact]
    public async Task ValidateTokenAsync_ExpiredToken_ReturnsNull()
    {
        // Arrange — create an expired token directly using the JWT library
        var keyBytes = Encoding.UTF8.GetBytes(SigningKey);
        var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            securityKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature);

        var tokenHandler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("sub", _userId.ToString()) }),
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            NotBefore = now.AddMinutes(-10),
            IssuedAt = now.AddMinutes(-10),
            Expires = now.AddMinutes(-5), // Expired 5 minutes ago
            SigningCredentials = credentials
        };
        var jwtToken = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        jwtToken.Header["kid"] = "default";
        var expiredToken = tokenHandler.WriteToken(jwtToken);

        // Act
        var principal = await _sut.ValidateTokenAsync(expiredToken);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WrongKey_ReturnsNull()
    {
        // Arrange — generate with one key, validate with another
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Change the validation key to a different key
        var wrongKeyBytes = Encoding.UTF8.GetBytes("ADifferentKeyThatIs32BytesLong!!");
        var wrongKeyInfo = new SigningKeyInfo("default", wrongKeyBytes, "HS256");
        _signingKeyProvider.GetValidationKeyAsync("default").Returns(wrongKeyInfo);

        // Act
        var principal = await _sut.ValidateTokenAsync(token!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_NullToken_ReturnsNull()
    {
        // Act
        var principal = await _sut.ValidateTokenAsync(null!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_EmptyToken_ReturnsNull()
    {
        // Act
        var principal = await _sut.ValidateTokenAsync("");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_MalformedToken_ReturnsNull()
    {
        // Act
        var principal = await _sut.ValidateTokenAsync("not.a.valid.jwt");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_UnknownKid_ReturnsNull()
    {
        // Arrange — generate a valid token, then make the key provider return null for its kid
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        _signingKeyProvider.GetValidationKeyAsync("default").Returns((SigningKeyInfo?)null);

        // Act
        var principal = await _sut.ValidateTokenAsync(token!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WrongIssuer_ReturnsNull()
    {
        // Arrange — generate with "TestIssuer", validate with service expecting "OtherIssuer"
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Create a new service with different issuer for validation
        var differentOptions = Options.Create(new AuthOptions
        {
            JwtSigningKey = SigningKey,
            Issuer = "OtherIssuer",
            Audience = "TestAudience",
            TokenExpirationMinutes = 60
        });
        var differentSut = new TokenService(_userRepository, _userRoleRepository, _signingKeyProvider, differentOptions);

        // Act
        var principal = await differentSut.ValidateTokenAsync(token!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WrongAudience_ReturnsNull()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Create a new service with different audience for validation
        var differentOptions = Options.Create(new AuthOptions
        {
            JwtSigningKey = SigningKey,
            Issuer = "TestIssuer",
            Audience = "OtherAudience",
            TokenExpirationMinutes = 60
        });
        var differentSut = new TokenService(_userRepository, _userRoleRepository, _signingKeyProvider, differentOptions);

        // Act
        var principal = await differentSut.ValidateTokenAsync(token!);

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task GenerateTokenAsync_InactiveUser_ReturnsNull()
    {
        // Arrange — user exists but is inactive
        var user = new UserDto(_userId, "ext-" + _userId, "user@test.com", "Test User", IsActive: false);
        _userRepository.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));
        SetupTenantRoles();
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert — inactive users must not get tokens, even if other data resolves
        Assert.Null(token);
    }

    [Fact]
    public async Task GenerateTokenAsync_RoleWithNullName_FilteredOut()
    {
        // Arrange — UserRoleDto.RoleName is nullable; ensure null names don't crash or appear
        SetupUser(_userId, "user@test.com", "Test User");
        var rolesWithNull = new List<UserRoleDto>
        {
            new(Guid.NewGuid(), _userId, Guid.NewGuid(), _tenantId, "Editor"),
            new(Guid.NewGuid(), _userId, Guid.NewGuid(), _tenantId, RoleName: null)
        };
        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(rolesWithNull));
        SetupSystemRoles();

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert — token generated, null role name omitted, "Editor" present
        Assert.NotNull(token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
        Assert.Single(roles);
        Assert.Equal("Editor", roles[0]);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsExpClaim_MatchesConfiguredExpiration()
    {
        // Arrange
        SetupUser(_userId, "user@test.com", "Test User");
        SetupTenantRoles();
        SetupSystemRoles();
        var before = DateTimeOffset.UtcNow;

        // Act
        var token = await _sut.GenerateTokenAsync(_userId, _tenantId);

        // Assert — exp claim is roughly now + 60 minutes (allow 30s tolerance for test slowness)
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expClaim = jwt.Claims.First(c => c.Type == "exp");
        var expSeconds = long.Parse(expClaim.Value, System.Globalization.CultureInfo.InvariantCulture);
        var expTime = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        var expectedExp = before.AddMinutes(60);
        Assert.InRange(expTime, expectedExp.AddSeconds(-30), expectedExp.AddSeconds(30));
    }

    [Fact]
    public async Task ValidateTokenAsync_WhitespaceToken_ReturnsNull()
    {
        // Act
        var principal = await _sut.ValidateTokenAsync("   ");

        // Assert
        Assert.Null(principal);
    }

    [Fact]
    public async Task ValidateTokenAsync_TokenWithoutKidHeader_ReturnsNull()
    {
        // Arrange — create a token without a kid header
        var keyBytes = Encoding.UTF8.GetBytes(SigningKey);
        var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes);
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            securityKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("sub", _userId.ToString()) }),
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = credentials
        };
        var jwtToken = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
        // Deliberately NOT setting kid header
        var tokenString = tokenHandler.WriteToken(jwtToken);

        // Act
        var principal = await _sut.ValidateTokenAsync(tokenString);

        // Assert — implementation requires kid; tokens without one are rejected
        Assert.Null(principal);
    }

    // --- Helper methods ---

    private void SetupUser(Guid userId, string email, string displayName)
    {
        var user = new UserDto(userId, "ext-" + userId, email, displayName, true);
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(user));
    }

    private void SetupTenantRoles(params (string RoleName, Guid RoleId)[] roles)
    {
        var roleDtos = roles.Select(r => new UserRoleDto(Guid.NewGuid(), _userId, r.RoleId, _tenantId, r.RoleName)).ToList();
        _userRoleRepository.GetByUserIdForTenantAsync(_userId, _tenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(roleDtos));
    }

    private void SetupSystemRoles(params (string RoleName, Guid RoleId)[] roles)
    {
        var roleDtos = roles.Select(r => new UserRoleDto(Guid.NewGuid(), _userId, r.RoleId, Guid.Empty, r.RoleName)).ToList();
        _userRoleRepository.GetSystemRolesForUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(roleDtos));
    }
}
