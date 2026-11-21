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
    public async Task SeedAsync_CallsEnsureDefinitionAsync_ExactlySevenTimes()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(7).EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesTokenLifetimeSetting_WithBoundsAndSystemAndTenantLevels()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.session.token-lifetime-minutes"
                && r.DataType == GroundUp.Core.Enums.SettingDataType.Int
                && r.DefaultValue == "60"
                && r.MinValue == "2"
                && r.MaxValue == "120"
                && r.GroupDisplayName == "Security > Sessions"
                && r.AllowedLevelNames.SequenceEqual(new[] { "system", "tenant" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesAbsoluteLifetimeSetting_WithBoundsAndSystemAndTenantLevels()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.session.absolute-lifetime-minutes"
                && r.DataType == GroundUp.Core.Enums.SettingDataType.Int
                && r.DefaultValue == "480"
                && r.MinValue == "10"
                && r.MaxValue == "1440"
                && r.GroupDisplayName == "Security > Sessions"
                && r.AllowedLevelNames.SequenceEqual(new[] { "system", "tenant" })),
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
    public async Task SeedAsync_CreatesDefaultTenantSlugSetting()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.application.default-tenant-slug"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_CreatesSignupCreatesOrgSetting()
    {
        // Act
        await _sut.SeedAsync();

        // Assert
        await _settingsService.Received(1).EnsureDefinitionAsync(
            Arg.Is<EnsureSettingDefinitionRequest>(r =>
                r.Key == "auth.application.signup-creates-org"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_SecondRun_IsIdempotent()
    {
        // Act — run twice
        await _sut.SeedAsync();
        await _sut.SeedAsync();

        // Assert — 14 total calls (7 per run), all succeed without error
        await _settingsService.Received(14).EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Order_Returns30()
    {
        _sut.Order.Should().Be(30);
    }
}
