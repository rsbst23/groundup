using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

public sealed class KeycloakIdTokenValidatorTests : IDisposable
{
    private const string Realm = "groundup";
    private const string ClientId = "groundup-app";
    private const string Nonce = "nonce-123";
    private const string InternalIssuer = "http://keycloak:8080/realms/groundup";
    private const string PublicIssuer = "https://login.example.com/realms/groundup";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;
    private readonly JwksHandler _jwks;
    private readonly KeycloakIdTokenValidator _sut;

    public KeycloakIdTokenValidatorTests()
    {
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "key-1" };
        _jwks = new JwksHandler();
        _jwks.Keys.Add(_signingKey);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("KeycloakIdp").Returns(_ => new HttpClient(_jwks, disposeHandler: false));

        var options = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        options.CurrentValue.Returns(new KeycloakOptions
        {
            InternalBaseUrl = "http://keycloak:8080",
            PublicBaseUrl = "https://login.example.com",
            SharedRealmName = Realm,
            AppClientId = ClientId
        });

        _sut = new KeycloakIdTokenValidator(options, new JwksCache(factory), NullLogger<KeycloakIdTokenValidator>.Instance);
    }

    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsSubjectAndAuthTime()
    {
        var authTime = DateTimeOffset.UtcNow.AddMinutes(-90);

        var result = await _sut.ValidateAsync(CreateToken(authTime: authTime), Nonce, realm: null);

        result.Success.Should().BeTrue();
        result.Data!.Subject.Should().Be("kc-user-1");
        result.Data.AuthTime!.Value.ToUnixTimeSeconds().Should().Be(authTime.ToUnixTimeSeconds());
        _jwks.Requests.Should().ContainSingle().Which.Should().Be($"{InternalIssuer}/protocol/openid-connect/certs");
    }

    [Fact]
    public async Task ValidateAsync_PublicUrlIssuer_IsAccepted()
    {
        var result = await _sut.ValidateAsync(CreateToken(issuer: PublicIssuer), Nonce, Realm);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NoAuthTimeClaim_ReturnsNullAuthTime()
    {
        var result = await _sut.ValidateAsync(CreateToken(includeAuthTime: false), Nonce, Realm);

        result.Success.Should().BeTrue();
        result.Data!.AuthTime.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_SignedWithAnotherKey_IsInvalid()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "key-1" };

        var result = await _sut.ValidateAsync(CreateToken(key: otherKey), Nonce, Realm);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_UnsignedToken_IsInvalid()
    {
        var payload = new JwtPayload(PublicIssuer, ClientId, new[] { new Claim("sub", "kc-user-1"), new Claim("nonce", Nonce) },
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5));
        var unsigned = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(), payload));

        var result = await _sut.ValidateAsync(unsigned, Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_WrongIssuer_IsInvalid()
    {
        var result = await _sut.ValidateAsync(CreateToken(issuer: "http://keycloak:8080/realms/other"), Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_WrongAudience_IsInvalid()
    {
        var result = await _sut.ValidateAsync(CreateToken(audience: "another-client"), Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_SeveralAudiencesWithoutMatchingAzp_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            CreateToken(extraClaims: new[] { new Claim("aud", "another-client"), new Claim("azp", "another-client") }),
            Nonce,
            Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_Expired_IsInvalid()
    {
        var result = await _sut.ValidateAsync(CreateToken(expires: DateTime.UtcNow.AddMinutes(-5)), Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_WrongNonce_ReturnsNonceMismatch()
    {
        var result = await _sut.ValidateAsync(CreateToken(), "another-nonce", Realm);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(IdTokenErrorCodes.NonceMismatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task ValidateAsync_MissingOrMalformed_IsInvalid(string? idToken)
    {
        var result = await _sut.ValidateAsync(idToken, Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    [Fact]
    public async Task ValidateAsync_KeyRotated_RefreshesJwksAndValidates()
    {
        // Arrange — the cache holds key-1, then Keycloak rotates to key-2
        await _sut.ValidateAsync(CreateToken(), Nonce, Realm);
        using var rotatedRsa = RSA.Create(2048);
        var rotated = new RsaSecurityKey(rotatedRsa) { KeyId = "key-2" };
        _jwks.Keys.Clear();
        _jwks.Keys.Add(rotated);

        // Act — the refresh is rate-limited to once per 30 seconds per issuer, so use a fresh cache
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("KeycloakIdp").Returns(_ => new HttpClient(_jwks, disposeHandler: false));
        var options = Substitute.For<IOptionsMonitor<KeycloakOptions>>();
        options.CurrentValue.Returns(new KeycloakOptions
        {
            InternalBaseUrl = "http://keycloak:8080",
            PublicBaseUrl = "https://login.example.com",
            SharedRealmName = Realm,
            AppClientId = ClientId
        });
        var sut = new KeycloakIdTokenValidator(options, new JwksCache(factory), NullLogger<KeycloakIdTokenValidator>.Instance);
        var result = await sut.ValidateAsync(CreateToken(key: rotated), Nonce, Realm);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_JwksUnreachable_IsInvalid()
    {
        _jwks.StatusCode = HttpStatusCode.ServiceUnavailable;

        var result = await _sut.ValidateAsync(CreateToken(), Nonce, Realm);

        result.ErrorCode.Should().Be(IdTokenErrorCodes.Invalid);
    }

    private string CreateToken(
        string issuer = InternalIssuer,
        string audience = ClientId,
        DateTime? expires = null,
        DateTimeOffset? authTime = null,
        bool includeAuthTime = true,
        SecurityKey? key = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "kc-user-1"),
            new("nonce", Nonce)
        };

        if (includeAuthTime)
        {
            claims.Add(new Claim("auth_time",
                (authTime ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64));
        }

        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: exp.AddMinutes(-10),
            expires: exp,
            signingCredentials: new SigningCredentials(key ?? _signingKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _jwks.Dispose();
    }

    /// <summary>Serves a JWKS document built from <see cref="Keys"/>.</summary>
    private sealed class JwksHandler : HttpMessageHandler
    {
        public List<RsaSecurityKey> Keys { get; } = new();
        public List<string> Requests { get; } = new();
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (StatusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(StatusCode));
            }

            var jwks = string.Join(",", Keys.Select(k =>
            {
                var p = k.Rsa.ExportParameters(false);
                return $"{{\"kty\":\"RSA\",\"use\":\"sig\",\"alg\":\"RS256\",\"kid\":\"{k.KeyId}\"," +
                       $"\"n\":\"{Base64UrlEncoder.Encode(p.Modulus)}\",\"e\":\"{Base64UrlEncoder.Encode(p.Exponent)}\"}}";
            }));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"keys\":[{jwks}]}}", Encoding.UTF8, "application/json")
            });
        }
    }
}
