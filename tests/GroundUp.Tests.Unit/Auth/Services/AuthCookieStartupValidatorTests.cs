using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class AuthCookieStartupValidatorTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ILogger<AuthCookieStartupValidator> _logger = Substitute.For<ILogger<AuthCookieStartupValidator>>();

    public AuthCookieStartupValidatorTests()
    {
        SetDomain("groundup.test");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task StartAsync_CookieSecureFalseOutsideDevelopment_Throws(string environment)
    {
        var sut = CreateSut(cookieSecure: false, environment);

        var act = () => sut.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("CookieSecure");
    }

    [Fact]
    public async Task StartAsync_CookieSecureFalseInDevelopment_WarnsAndStarts()
    {
        var sut = CreateSut(cookieSecure: false, Environments.Development);

        await sut.StartAsync(CancellationToken.None);

        _logger.ReceivedWithAnyArgs().Log(LogLevel.Warning, default, default!, default, default!);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task StartAsync_CookieSecureTrue_StartsWithoutWarning(string environment)
    {
        var sut = CreateSut(cookieSecure: true, environment);

        await sut.StartAsync(CancellationToken.None);

        _logger.DidNotReceiveWithAnyArgs().Log(LogLevel.Warning, default, default!, default, default!);
    }

    [Fact]
    public async Task StartAsync_SingleLabelDefaultDomain_LogsWarning()
    {
        SetDomain("localhost");
        var sut = CreateSut(cookieSecure: true, "Production");

        await sut.StartAsync(CancellationToken.None);

        _logger.ReceivedWithAnyArgs().Log(LogLevel.Warning, default, default!, default, default!);
    }

    [Fact]
    public async Task StartAsync_SettingsUnavailable_StartsWithoutThrowing()
    {
        _settings.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<OperationResult<string>>>(_ => throw new InvalidOperationException("database not ready"));
        var sut = CreateSut(cookieSecure: true, "Production");

        var act = () => sut.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData(" localhost ", true)]
    [InlineData(".localhost", true)]
    [InlineData("groundup.test", false)]
    [InlineData("app.example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSingleLabelDomain_ReturnsExpected(string? domain, bool expected)
    {
        AuthCookieStartupValidator.IsSingleLabelDomain(domain).Should().Be(expected);
    }

    private void SetDomain(string value) =>
        _settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok(value));

    private AuthCookieStartupValidator CreateSut(bool cookieSecure, string environment)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_settings);
        var provider = services.BuildServiceProvider();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);

        return new AuthCookieStartupValidator(
            Options.Create(new AuthOptions { CookieSecure = cookieSecure }),
            env,
            provider.GetRequiredService<IServiceScopeFactory>(),
            _logger);
    }
}
