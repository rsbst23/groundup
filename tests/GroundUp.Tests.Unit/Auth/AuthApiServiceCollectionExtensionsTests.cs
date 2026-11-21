using System.Security.Claims;
using FluentAssertions;
using GroundUp.Auth.Api;
using GroundUp.Auth.Services.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth;

public sealed class AuthApiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGroundUpAuthApi_ConfiguresHeaderAndCookie()
    {
        using var provider = BuildProvider(cookieSecure: true);

        var options = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        options.HeaderName.Should().Be("X-CSRF-Token");
        options.Cookie.Name.Should().Be("CsrfToken");
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SameSite.Should().Be(SameSiteMode.Strict);
        options.Cookie.Path.Should().Be("/");
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Fact]
    public void AddGroundUpAuthApi_CookieSecureFalse_SecurePolicyNone()
    {
        using var provider = BuildProvider(cookieSecure: false);

        var options = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.None);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GetAndStoreTokens_WritesCsrfCookieWithExpectedAttributes(bool cookieSecure, bool expectSecure)
    {
        // Arrange: real antiforgery services, a signed-in user
        using var provider = BuildProvider(cookieSecure);
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "GroundUp"));

        // Act
        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

        // Assert
        tokens.RequestToken.Should().NotBeNullOrEmpty();
        var header = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        header.Should().StartWith("csrftoken=");
        header.Should().Contain("httponly");
        header.Should().Contain("samesite=strict");
        header.Should().Contain("path=/");
        header.Should().NotContain("domain=");
        header.Contains("secure").Should().Be(expectSecure);
    }

    [Fact]
    public void UseGroundUpAuth_AntiforgeryNotRegistered_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseGroundUpAuth();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddGroundUpAuthApi*");
    }

    private static ServiceProvider BuildProvider(bool cookieSecure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.Configure<AuthOptions>(o => o.CookieSecure = cookieSecure);
        services.AddGroundUpAuthApi();
        return services.BuildServiceProvider();
    }
}
