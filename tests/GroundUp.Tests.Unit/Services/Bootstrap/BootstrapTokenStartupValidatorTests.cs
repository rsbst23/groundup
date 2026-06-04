using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Services.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Bootstrap;

/// <summary>
/// Unit tests for <see cref="BootstrapTokenStartupValidator"/>.
/// Validates that the hosted service throws on missing token during setup mode
/// and is a no-op when setup is already complete.
/// Requirements: 8.6, 8.9
/// </summary>
public sealed class BootstrapTokenStartupValidatorTests
{
    private readonly IBootstrapStateService _bootstrapStateService;

    public BootstrapTokenStartupValidatorTests()
    {
        _bootstrapStateService = Substitute.For<IBootstrapStateService>();
    }

    [Fact]
    public async Task StartAsync_IncompleteAndTokenMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        var configuration = BuildConfiguration(bootstrapAdminToken: null);
        var sut = CreateSut(configuration);

        // Act
        var act = () => sut.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*BootstrapAdminToken*");
    }

    [Fact]
    public async Task StartAsync_IncompleteAndTokenWhitespace_ThrowsInvalidOperationException()
    {
        // Arrange
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        var configuration = BuildConfiguration(bootstrapAdminToken: "   ");
        var sut = CreateSut(configuration);

        // Act
        var act = () => sut.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*BootstrapAdminToken*");
    }

    [Fact]
    public async Task StartAsync_IncompleteAndTokenConfigured_DoesNotThrow()
    {
        // Arrange
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        var configuration = BuildConfiguration(
            bootstrapAdminToken: "a-valid-token-that-is-at-least-32-chars-long!");
        var sut = CreateSut(configuration);

        // Act
        var act = () => sut.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Complete_DoesNotThrow_RegardlessOfToken()
    {
        // Arrange — setup is complete, token is NOT configured
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var configuration = BuildConfiguration(bootstrapAdminToken: null);
        var sut = CreateSut(configuration);

        // Act
        var act = () => sut.StartAsync(CancellationToken.None);

        // Assert — Req 8.9: post-setup, token is not required
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Complete_DoesNotCheckToken()
    {
        // Arrange — setup is complete
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var configuration = BuildConfiguration(bootstrapAdminToken: null);
        var sut = CreateSut(configuration);

        // Act
        await sut.StartAsync(CancellationToken.None);

        // Assert — should have returned early after IsCompleteAsync
        await _bootstrapStateService.Received(1).IsCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_IsNoOp()
    {
        // Arrange
        var configuration = BuildConfiguration(bootstrapAdminToken: null);
        var sut = CreateSut(configuration);

        // Act
        var act = () => sut.StopAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #region Helpers

    private BootstrapTokenStartupValidator CreateSut(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _bootstrapStateService);
        var serviceProvider = services.BuildServiceProvider();

        return new BootstrapTokenStartupValidator(serviceProvider, configuration);
    }

    private static IConfiguration BuildConfiguration(string? bootstrapAdminToken)
    {
        var configData = new Dictionary<string, string?>();

        if (bootstrapAdminToken is not null)
        {
            configData["GroundUp:BootstrapAdminToken"] = bootstrapAdminToken;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    #endregion
}
