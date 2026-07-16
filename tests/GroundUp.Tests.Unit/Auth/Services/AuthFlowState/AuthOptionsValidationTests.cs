using FluentAssertions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Auth.Services.AuthFlowState;

public sealed class AuthOptionsValidationTests
{
    private const string ValidSigningKey = "ThisIsAValidSigningKeyThatIs32BytesLong!!";

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CleanupIntervalMinutes_ZeroOrNegative_FailsValidation(int interval)
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.CleanupIntervalMinutes = interval;
            opts.RetentionDays = 7;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*CleanupIntervalMinutes*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-7)]
    public void RetentionDays_Negative_FailsValidation(int days)
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.CleanupIntervalMinutes = 5;
            opts.RetentionDays = days;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RetentionDays*");
    }

    [Fact]
    public void ValidOptions_PassesValidation()
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.CleanupIntervalMinutes = 5;
            opts.RetentionDays = 7;
        });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RetentionDaysZero_PassesValidation()
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.CleanupIntervalMinutes = 1;
            opts.RetentionDays = 0;
        });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AbsoluteSessionLifetimeMinutes_Zero_FailsValidation()
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.AbsoluteSessionLifetimeMinutes = 0;
            opts.TokenExpirationMinutes = 0;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*AbsoluteSessionLifetimeMinutes*");
    }

    [Theory]
    [InlineData(30, 60)]  // 30 < 60
    [InlineData(1, 60)]   // 1 < 60
    [InlineData(59, 60)]  // 59 < 60
    public void AbsoluteSessionLifetimeMinutes_LessThanTokenExpiration_FailsValidation(
        int absoluteLifetime, int tokenExpiration)
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.AbsoluteSessionLifetimeMinutes = absoluteLifetime;
            opts.TokenExpirationMinutes = tokenExpiration;
        });

        // Assert
        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*AbsoluteSessionLifetimeMinutes*");
    }

    [Fact]
    public void AbsoluteSessionLifetimeMinutes_EqualToTokenExpiration_PassesValidation()
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.AbsoluteSessionLifetimeMinutes = 60;
            opts.TokenExpirationMinutes = 60;
        });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AbsoluteSessionLifetimeMinutes_GreaterThanTokenExpiration_PassesValidation()
    {
        // Arrange & Act
        var act = () => BuildAndResolveOptions(opts =>
        {
            opts.JwtSigningKey = ValidSigningKey;
            opts.AbsoluteSessionLifetimeMinutes = 480;
            opts.TokenExpirationMinutes = 60;
        });

        // Assert
        act.Should().NotThrow();
    }

    private static AuthOptions BuildAndResolveOptions(Action<AuthOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddGroundUpAuth(configure);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AuthOptions>>().Value;
    }
}
