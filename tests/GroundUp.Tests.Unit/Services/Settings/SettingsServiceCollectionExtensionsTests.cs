using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGroundUpSettings_RegistersISettingsServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingsService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(SettingsService));
    }

    [Fact]
    public void AddGroundUpSettings_WorksWithoutEncryptionProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert — no ISettingEncryptionProvider registered, but AddGroundUpSettings doesn't throw
        var encryptionDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingEncryptionProvider));
        encryptionDescriptor.Should().BeNull();

        var settingsDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingsService));
        settingsDescriptor.Should().NotBeNull();
    }
}
