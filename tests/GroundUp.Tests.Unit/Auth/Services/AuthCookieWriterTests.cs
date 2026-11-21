using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class AuthCookieWriterTests
{
    private readonly ISettingsService _settingsService;
    private readonly AuthOptions _authOptions;
    private readonly AuthCookieWriter _sut;

    public AuthCookieWriterTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _authOptions = new AuthOptions
        {
            CookieName = "AuthToken",
            CookieSecure = true,
            CookieSameSite = SameSiteMode.Strict,
            TokenExpirationMinutes = 60
        };

        var options = Options.Create(_authOptions);
        _sut = new AuthCookieWriter(_settingsService, options);

        // Default: domain setting returns "sampleapp.com"
        SetupDomainSetting("sampleapp.com");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_SameSiteStrict_SetsCookieWithStrictSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.Strict;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=strict");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_SameSiteLax_SetsCookieWithLaxSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.Lax;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=lax");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_SameSiteNone_SetsCookieWithNoneSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.None;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=none");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_WhitespaceOnlyDomain_OmitsDomainAttribute()
    {
        // Arrange
        SetupDomainSetting("   ");
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().NotContain("domain=");
    }

    [Fact]
    public async Task ClearAuthCookieAsync_ProducesSameDomainAsWrite()
    {
        // Arrange
        SetupDomainSetting("example.com");
        var writeContext = new DefaultHttpContext();
        var clearContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(writeContext, "test-token");
        await _sut.ClearAuthCookieAsync(clearContext);

        // Assert
        var writeHeader = writeContext.Response.Headers.SetCookie.ToString();
        var clearHeader = clearContext.Response.Headers.SetCookie.ToString();

        // Both should contain the same domain attribute
        writeHeader.Should().Contain("domain=.example.com");
        clearHeader.Should().Contain("domain=.example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteAuthCookieAsync_BlankToken_ThrowsArgumentException(string? token)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.WriteAuthCookieAsync(httpContext, token!);

        // Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("token");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_ValidToken_SetsCookieWithCorrectAttributes()
    {
        // Arrange
        SetupDomainSetting("sampleapp.com");
        _authOptions.CookieName = "AuthToken";
        _authOptions.CookieSecure = true;
        _authOptions.CookieSameSite = SameSiteMode.Strict;
        _authOptions.TokenExpirationMinutes = 60;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "my-jwt-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        // Cookie name and value
        setCookieHeader.Should().StartWith("AuthToken=my-jwt-token");

        // Domain
        setCookieHeader.Should().Contain("domain=.sampleapp.com");

        // Path
        setCookieHeader.Should().Contain("path=/");

        // HttpOnly
        setCookieHeader.Should().Contain("httponly");

        // Secure
        setCookieHeader.Should().Contain("secure");

        // SameSite
        setCookieHeader.Should().Contain("samesite=strict");

        // Expires should be set (persistent cookie)
        setCookieHeader.Should().Contain("expires=");
    }

    [Fact]
    public async Task WriteAuthCookieAsync_JwtToken_CookieExpiresWhenTokenExpires()
    {
        // Arrange: a token that expires in 17 minutes, unlike the configured 60
        var tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(17);
        var token = CreateJwt(tokenExpiry);
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, token);

        // Assert
        var cookieExpiry = ReadExpires(httpContext.Response.Headers.SetCookie.ToString());
        cookieExpiry.Should().BeCloseTo(tokenExpiry, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteAuthCookieAsync_NotAJwt_FallsBackToConfiguredLifetime()
    {
        // Arrange
        _authOptions.TokenExpirationMinutes = 45;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteAuthCookieAsync(httpContext, "not-a-jwt");

        // Assert
        var cookieExpiry = ReadExpires(httpContext.Response.Headers.SetCookie.ToString());
        cookieExpiry.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(45), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WriteIdTokenCookieAsync_AfterAuthCookie_ExpiresWithTheAuthCookie()
    {
        // Arrange
        var tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(23);
        var httpContext = new DefaultHttpContext();
        await _sut.WriteAuthCookieAsync(httpContext, CreateJwt(tokenExpiry));
        httpContext.Response.Headers.Remove("Set-Cookie");

        // Act
        await _sut.WriteIdTokenCookieAsync(httpContext, "id-token-value");

        // Assert
        var header = httpContext.Response.Headers.SetCookie.ToString();
        ReadExpires(header).Should().BeCloseTo(tokenExpiry, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteIdTokenCookieAsync_NoAuthCookieWritten_FallsBackToConfiguredLifetime()
    {
        // Arrange
        _authOptions.TokenExpirationMinutes = 30;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteIdTokenCookieAsync(httpContext, "id-token-value");

        // Assert
        var header = httpContext.Response.Headers.SetCookie.ToString();
        ReadExpires(header).Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(true, SameSiteMode.Strict, "samesite=strict")]
    [InlineData(true, SameSiteMode.Lax, "samesite=lax")]
    public async Task WriteIdTokenCookieAsync_UsesSameAttributesAsAuthCookie(bool secure, SameSiteMode sameSite, string expectedSameSite)
    {
        // Arrange
        _authOptions.CookieSecure = secure;
        _authOptions.CookieSameSite = sameSite;
        _authOptions.IdTokenCookieName = "IdToken";
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteIdTokenCookieAsync(httpContext, "id-token-value");

        // Assert
        var header = httpContext.Response.Headers.SetCookie.ToString();
        header.Should().StartWith("IdToken=id-token-value");
        header.Should().Contain("httponly");
        header.Should().Contain("secure");
        header.Should().Contain(expectedSameSite);
        header.Should().Contain("path=/");
        header.Should().Contain("domain=.sampleapp.com");
    }

    [Fact]
    public async Task WriteIdTokenCookieAsync_SecureDisabled_OmitsSecureLikeTheAuthCookie()
    {
        // Arrange: one setting controls Secure for every cookie
        _authOptions.CookieSecure = false;
        var httpContext = new DefaultHttpContext();

        // Act
        await _sut.WriteIdTokenCookieAsync(httpContext, "id-token-value");

        // Assert
        httpContext.Response.Headers.SetCookie.ToString().Should().NotContain("secure");
    }

    [Fact]
    public async Task WriteIdTokenCookieAsync_BlankIdToken_WritesNothing()
    {
        var httpContext = new DefaultHttpContext();

        await _sut.WriteIdTokenCookieAsync(httpContext, "  ");

        httpContext.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ClearIdTokenCookieAsync_ExpiresCookieWithSameDomain()
    {
        var httpContext = new DefaultHttpContext();

        await _sut.ClearIdTokenCookieAsync(httpContext);

        var header = httpContext.Response.Headers.SetCookie.ToString();
        header.Should().Contain("domain=.sampleapp.com");
        ReadExpires(header).Should().BeBefore(DateTimeOffset.UtcNow);
    }

    // --- Helper methods ---

    private static string CreateJwt(DateTimeOffset expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("ThisIsAValidSigningKeyThatIs32BytesLong!!"));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "test",
            Audience = "test",
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateJwtSecurityToken(descriptor));
    }

    private static DateTimeOffset ReadExpires(string setCookieHeader)
    {
        var part = setCookieHeader.Split(';')
            .Select(p => p.Trim())
            .First(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return DateTimeOffset.Parse(
            part["expires=".Length..],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);
    }

    private void SetupDomainSetting(string? value)
    {
        _settingsService.GetAsync<string>(
                "auth.application.default-domain",
                Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok(value!));
    }
}
