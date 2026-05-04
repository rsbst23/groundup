using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Events;
using GroundUp.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Expanded DI registration tests verifying that AddGroundUpSettings registers
/// all expected services with the correct lifetimes and implementations.
/// </summary>
public sealed class SettingsServiceRegistrationTests
{
    [Fact]
    public void AddGroundUpSettings_RegistersIScopeChainProviderAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IScopeChainProvider));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(DefaultScopeChainProvider));
    }

    [Fact]
    public void AddGroundUpSettings_RegistersISettingsAdminServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingsAdminService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(SettingsAdminService));
    }

    [Fact]
    public void AddGroundUpSettings_RegistersSettingsCacheKeyTrackerAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SettingsCacheKeyTracker));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddGroundUpSettings_RegistersIMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));
        descriptor.Should().NotBeNull();
    }

    [Fact]
    public void AddGroundUpSettings_RegistersSettingsCacheInvalidationHandler()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGroundUpSettings();

        // Assert
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEventHandler<SettingChangedEvent>));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(SettingsCacheInvalidationHandler));
    }

    [Fact]
    public void AddGroundUpSettings_IScopeChainProviderIsOverridable()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register a custom IScopeChainProvider BEFORE calling AddGroundUpSettings
        services.AddScoped<IScopeChainProvider, CustomTestScopeChainProvider>();

        // Act
        services.AddGroundUpSettings();

        // Assert — TryAddScoped should NOT overwrite the custom registration
        var descriptors = services.Where(d => d.ServiceType == typeof(IScopeChainProvider)).ToList();
        descriptors.Should().HaveCount(1);
        descriptors[0].ImplementationType.Should().Be(typeof(CustomTestScopeChainProvider));
    }

    [Fact]
    public void AddGroundUpSettings_ConfiguresCacheOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var customTtl = TimeSpan.FromMinutes(30);

        // Act
        services.AddGroundUpSettings(options =>
        {
            options.CacheDuration = customTtl;
        });

        // Assert — build the provider and resolve the options
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsCacheOptions>>();
        options.Value.CacheDuration.Should().Be(customTtl);
    }

    /// <summary>
    /// Test-only IScopeChainProvider implementation to verify TryAddScoped behavior.
    /// </summary>
    private sealed class CustomTestScopeChainProvider : IScopeChainProvider
    {
        public Task<IReadOnlyList<SettingScopeEntry>> GetScopeChainAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SettingScopeEntry>>(Array.Empty<SettingScopeEntry>());
        }
    }
}
