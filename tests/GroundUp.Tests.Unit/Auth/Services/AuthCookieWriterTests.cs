using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
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
    public void WriteAuthCookie_SameSiteStrict_SetsCookieWithStrictSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.Strict;
        var httpContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=strict");
    }

    [Fact]
    public void WriteAuthCookie_SameSiteLax_SetsCookieWithLaxSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.Lax;
        var httpContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=lax");
    }

    [Fact]
    public void WriteAuthCookie_SameSiteNone_SetsCookieWithNoneSameSite()
    {
        // Arrange
        _authOptions.CookieSameSite = SameSiteMode.None;
        var httpContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("samesite=none");
    }

    [Fact]
    public void WriteAuthCookie_WhitespaceOnlyDomain_OmitsDomainAttribute()
    {
        // Arrange
        SetupDomainSetting("   ");
        var httpContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(httpContext, "test-token");

        // Assert
        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().NotContain("domain=");
    }

    [Fact]
    public void ClearAuthCookie_ProducesSameDomainAsWrite()
    {
        // Arrange
        SetupDomainSetting("example.com");
        var writeContext = new DefaultHttpContext();
        var clearContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(writeContext, "test-token");
        _sut.ClearAuthCookie(clearContext);

        // Assert
        var writeHeader = writeContext.Response.Headers.SetCookie.ToString();
        var clearHeader = clearContext.Response.Headers.SetCookie.ToString();

        // Both should contain the same domain attribute
        writeHeader.Should().Contain("domain=.example.com");
        clearHeader.Should().Contain("domain=.example.com");
    }

    [Fact]
    public void WriteAuthCookie_NullToken_ThrowsArgumentException()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.WriteAuthCookie(httpContext, null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("token");
    }

    [Fact]
    public void WriteAuthCookie_EmptyToken_ThrowsArgumentException()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.WriteAuthCookie(httpContext, string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("token");
    }

    [Fact]
    public void WriteAuthCookie_WhitespaceToken_ThrowsArgumentException()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.WriteAuthCookie(httpContext, "   ");

        // Assert
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("token");
    }

    [Fact]
    public void WriteAuthCookie_ValidToken_SetsCookieWithCorrectAttributes()
    {
        // Arrange
        SetupDomainSetting("sampleapp.com");
        _authOptions.CookieName = "AuthToken";
        _authOptions.CookieSecure = true;
        _authOptions.CookieSameSite = SameSiteMode.Strict;
        _authOptions.TokenExpirationMinutes = 60;
        var httpContext = new DefaultHttpContext();

        // Act
        _sut.WriteAuthCookie(httpContext, "my-jwt-token");

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

    // --- Helper methods ---

    private void SetupDomainSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _settingsService.GetAsync<string>(
                    "auth.application.default-domain",
                    Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(value));
        }
        else
        {
            _settingsService.GetAsync<string>(
                    "auth.application.default-domain",
                    Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(value));
        }
    }
}
