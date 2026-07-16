using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// Property-based tests for <see cref="AuthCookieWriter"/>.
/// Feature: phase-10c-auth-dispatcher
/// </summary>
[Trait("Category", "Property")]
public sealed class AuthCookieWriterPropertyTests
{
    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 1: Cookie Domain Derivation
    /// For any non-empty, non-whitespace domain string from settings, the cookie Domain
    /// attribute starts with "." and contains the trimmed domain value.
    /// For any empty, null, or whitespace-only domain, the cookie Domain is not set.
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthCookieWriterArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 1: Cookie Domain Derivation — non-empty domain prepends dot")]
    public Property WriteAuthCookie_NonEmptyDomain_PrependsDot(NonEmptyDomainSetting testCase)
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok(testCase.Domain));

        var options = Options.Create(new AuthOptions());
        var sut = new AuthCookieWriter(settingsService, options);

        var httpContext = new DefaultHttpContext();

        // Act
        sut.WriteAuthCookie(httpContext, "test-token-value");

        // Assert — the Set-Cookie header should contain a Domain attribute starting with "."
        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        var expectedDomain = $".{testCase.Domain.Trim()}";

        return (setCookieHeader.Contains($"domain={expectedDomain}", StringComparison.OrdinalIgnoreCase))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 1: Cookie Domain Derivation — empty/null/whitespace omits Domain
    /// For any empty, null, or whitespace-only domain setting value, the cookie Domain attribute is omitted.
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthCookieWriterArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 1: Cookie Domain Derivation — empty domain omits attribute")]
    public Property WriteAuthCookie_EmptyDomain_OmitsDomainAttribute(EmptyDomainSetting testCase)
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();

        if (testCase.ReturnsFailure)
        {
            settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Fail("Not found", 404));
        }
        else
        {
            settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(OperationResult<string>.Ok(testCase.Domain));
        }

        var options = Options.Create(new AuthOptions());
        var sut = new AuthCookieWriter(settingsService, options);

        var httpContext = new DefaultHttpContext();

        // Act
        sut.WriteAuthCookie(httpContext, "test-token-value");

        // Assert — the Set-Cookie header should NOT contain "domain="
        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();

        return (!setCookieHeader.Contains("domain=", StringComparison.OrdinalIgnoreCase))
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 2: Cookie Configuration Propagation
    /// For any valid AuthOptions values (Secure, SameSite, CookieName, TokenExpirationMinutes),
    /// the cookie written by WriteAuthCookie reflects those configuration values.
    /// **Validates: Requirements 1.4–1.10**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthCookieWriterArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 2: Cookie Configuration Propagation")]
    public Property WriteAuthCookie_AuthOptions_PropagateToAttributes(AuthOptionsCombination testCase)
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Not found", 404));

        var authOptions = new AuthOptions
        {
            CookieName = testCase.CookieName,
            CookieSecure = testCase.Secure,
            CookieSameSite = testCase.SameSite,
            TokenExpirationMinutes = testCase.TokenExpirationMinutes
        };
        var options = Options.Create(authOptions);
        var sut = new AuthCookieWriter(settingsService, options);

        var httpContext = new DefaultHttpContext();

        // Act
        sut.WriteAuthCookie(httpContext, "test-token-value");

        // Assert — validate cookie header contains the expected attributes
        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();

        var containsCookieName = setCookieHeader.StartsWith(testCase.CookieName + "=", StringComparison.OrdinalIgnoreCase);
        var containsHttpOnly = setCookieHeader.Contains("httponly", StringComparison.OrdinalIgnoreCase);
        var containsSecure = !testCase.Secure || setCookieHeader.Contains("secure", StringComparison.OrdinalIgnoreCase);
        var containsPath = setCookieHeader.Contains("path=/", StringComparison.OrdinalIgnoreCase);

        // SameSite mapping: Strict → "samesite=strict", Lax → "samesite=lax", None → "samesite=none"
        var expectedSameSite = testCase.SameSite switch
        {
            SameSiteMode.Strict => "samesite=strict",
            SameSiteMode.Lax => "samesite=lax",
            SameSiteMode.None => "samesite=none",
            _ => ""
        };
        var containsSameSite = string.IsNullOrEmpty(expectedSameSite)
            || setCookieHeader.Contains(expectedSameSite, StringComparison.OrdinalIgnoreCase);

        // Verify expires is set (the header should contain "expires=")
        var containsExpires = setCookieHeader.Contains("expires=", StringComparison.OrdinalIgnoreCase);

        return (containsCookieName && containsHttpOnly && containsSecure
            && containsPath && containsSameSite && containsExpires)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 3: Empty Token Rejection
    /// For any null, empty, or whitespace-only token string, WriteAuthCookie throws ArgumentException.
    /// **Validates: Requirements 1.11, 1.12**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AuthCookieWriterArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 3: Empty Token Rejection")]
    public Property WriteAuthCookie_EmptyToken_ThrowsArgumentException(InvalidTokenInput testCase)
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail("Not found", 404));

        var options = Options.Create(new AuthOptions());
        var sut = new AuthCookieWriter(settingsService, options);

        var httpContext = new DefaultHttpContext();

        // Act & Assert
        Exception? caught = null;
        try
        {
            sut.WriteAuthCookie(httpContext, testCase.Token!);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        return (caught is ArgumentException).ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents a non-empty, non-whitespace domain setting value.
/// </summary>
public sealed record NonEmptyDomainSetting(string Domain);

/// <summary>
/// Represents an empty, null, or whitespace-only domain setting value (or a failed result).
/// </summary>
public sealed record EmptyDomainSetting(string? Domain, bool ReturnsFailure);

/// <summary>
/// Represents a combination of AuthOptions values for configuration propagation testing.
/// </summary>
public sealed record AuthOptionsCombination(
    string CookieName,
    bool Secure,
    SameSiteMode SameSite,
    int TokenExpirationMinutes);

/// <summary>
/// Represents an invalid (null, empty, or whitespace-only) token input.
/// </summary>
public sealed record InvalidTokenInput(string? Token);

/// <summary>
/// Custom FsCheck Arbitrary generators for AuthCookieWriter property tests.
/// </summary>
public static class AuthCookieWriterArbitraries
{
    /// <summary>
    /// Generates valid, non-empty domain strings for cookie domain derivation testing.
    /// </summary>
    public static Arbitrary<NonEmptyDomainSetting> NonEmptyDomainSettingArb()
    {
        var gen = Gen.Elements(
            "sampleapp.com",
            "example.org",
            "myapp.io",
            "company.co.uk",
            "test-domain.net",
            "internal.corp",
            "localhost",
            "app.staging.example.com"
        ).Select(d => new NonEmptyDomainSetting(d));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates empty, null, or whitespace-only domain settings (or failed results).
    /// </summary>
    public static Arbitrary<EmptyDomainSetting> EmptyDomainSettingArb()
    {
        var gen = Gen.Elements(
            new EmptyDomainSetting(null, false),
            new EmptyDomainSetting("", false),
            new EmptyDomainSetting("   ", false),
            new EmptyDomainSetting("\t", false),
            new EmptyDomainSetting(" \n ", false),
            new EmptyDomainSetting(null, true)  // settings service returns failure
        );

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates various AuthOptions combinations for configuration propagation testing.
    /// </summary>
    public static Arbitrary<AuthOptionsCombination> AuthOptionsCombinationArb()
    {
        var gen = from cookieName in Gen.Elements("AuthToken", "MyAuth", "SessionCookie", "AppToken")
                  from secure in Arb.Generate<bool>()
                  from sameSite in Gen.Elements(SameSiteMode.Strict, SameSiteMode.Lax, SameSiteMode.None)
                  from expirationMinutes in Gen.Choose(1, 1440)
                  select new AuthOptionsCombination(cookieName, secure, sameSite, expirationMinutes);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates invalid (null, empty, or whitespace-only) token inputs.
    /// </summary>
    public static Arbitrary<InvalidTokenInput> InvalidTokenInputArb()
    {
        var gen = Gen.Elements(
            new InvalidTokenInput(null),
            new InvalidTokenInput(""),
            new InvalidTokenInput(" "),
            new InvalidTokenInput("   "),
            new InvalidTokenInput("\t"),
            new InvalidTokenInput("\n"),
            new InvalidTokenInput(" \t\n ")
        );

        return gen.ToArbitrary();
    }
}
