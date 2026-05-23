using FluentAssertions;
using GroundUp.Auth.Data.Postgres.Seeders;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Seeders;

/// <summary>
/// Unit tests for <see cref="DefaultAuthSettingsSeeder"/>.
/// Uses NSubstitute mocks since the seeder requires the full settings infrastructure
/// (GroundUpDbContext with settings tables) which is not available in the auth integration tests.
/// </summary>
public sealed class DefaultAuthSettingsSeederTests
{
    private readonly ISettingsService _settingsService;
    private readonly DefaultAuthSettingsSeeder _sut;

    public DefaultAuthSettingsSeederTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        var logger = Substitute.For<ILogger<DefaultAuthSettingsSeeder>>();

        _settingsService.EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(OperationResult<SettingDefinitionDto>.Ok(null!));

        _sut = new DefaultAuthSettingsSeeder(_settingsService, logger);
    }

    [Fact]
    public async Task SeedAsync_CallsEnsureDefinitionAsync_ExactlyThreeTimes()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(3).EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesDefaultDomainSetting()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.application.default-domain"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesSharedRealmNameSetting()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.keycloak.shared-realm-name"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesPublicBaseUrlSetting()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.keycloak.public-base-url"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_SecondRun_IsIdempotent()
    {
        // Act — run twice
        await _sut.SeedAsync();
        await _sut.SeedAsync();

        // Assert — 6 total calls (3 per run), all succeed without error
        await _settingsService.Received(6).EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Order_Returns30()
    {
        _sut.Order.Should().Be(30);
    }
}
