using FluentAssertions;
using GroundUp.Auth.Services;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// GU-87: a return URL is accepted only when it is a path on this site, so the post-sign-in
/// redirect can never send the user elsewhere.
/// </summary>
public sealed class ReturnUrlRulesTests
{
    private const string CallbackPath = "/auth/callback";

    [Theory]
    [InlineData("/")]
    [InlineData("/session")]
    [InlineData("/invite/abc?org=acme")]
    [InlineData("/a%20b")]
    [InlineData("/data?page=2&sort=name")]
    [InlineData("/auth/callbackish")]
    public void TryNormalize_PathOnThisSite_AcceptedUnchanged(string returnUrl)
    {
        ReturnUrlRules.TryNormalize(returnUrl, CallbackPath, out var normalized).Should().BeTrue();
        normalized.Should().Be(returnUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_NullOrBlank_AcceptedAsNoReturnUrl(string? returnUrl)
    {
        ReturnUrlRules.TryNormalize(returnUrl, CallbackPath, out var normalized).Should().BeTrue();
        normalized.Should().BeNull();
    }

    [Theory]
    [InlineData("http://evil.com")]
    [InlineData("https://evil.com/x")]
    [InlineData("//evil.com")]
    [InlineData("//evil.com/session")]
    [InlineData("/\\evil.com")]
    [InlineData("/a\\b")]
    [InlineData("/a#b")]
    [InlineData("/a\nb")]
    [InlineData("/a\rb")]
    [InlineData("/a\tb")]
    [InlineData("/a\u007fb")]
    [InlineData("javascript:alert(1)")]
    [InlineData("session")]
    [InlineData("/auth/callback")]
    [InlineData("/AUTH/CALLBACK?x=1")]
    [InlineData("/auth/callback/x")]
    [InlineData("/auth/callback?code=1&state=2")]
    public void TryNormalize_NotAPathOnThisSite_Refused(string returnUrl)
    {
        ReturnUrlRules.TryNormalize(returnUrl, CallbackPath, out var normalized).Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryNormalize_LongerThanTheColumn_Refused()
    {
        var returnUrl = "/" + new string('a', ReturnUrlRules.MaxLength);

        ReturnUrlRules.TryNormalize(returnUrl, CallbackPath, out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_ExactlyTheMaximumLength_Accepted()
    {
        var returnUrl = "/" + new string('a', ReturnUrlRules.MaxLength - 1);

        ReturnUrlRules.TryNormalize(returnUrl, CallbackPath, out var normalized).Should().BeTrue();
        normalized.Should().HaveLength(ReturnUrlRules.MaxLength);
    }

    [Fact]
    public void TryNormalize_CustomCallbackPath_ThatPathIsRefused()
    {
        ReturnUrlRules.TryNormalize("/signin-callback", "/signin-callback/", out _).Should().BeFalse();
        ReturnUrlRules.TryNormalize("/auth/callback", "/signin-callback", out _).Should().BeTrue();
    }
}
