using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Keycloak;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Keycloak;

/// <summary>
/// Tests for <see cref="KeycloakOptionsSetup"/>: settings map to options, and a change to a Keycloak
/// setting refreshes them. Phase 10b Properties 1 and 2 (Requirements 2.2, 2.4).
/// </summary>
public sealed class KeycloakOptionsSetupTests
{
    private static readonly string[] Keys =
    {
        KeycloakOptionsSetup.SettingKeys.PublicBaseUrl,
        KeycloakOptionsSetup.SettingKeys.SharedRealmName,
        KeycloakOptionsSetup.SettingKeys.InternalBaseUrl,
        KeycloakOptionsSetup.SettingKeys.AdminClientId,
        KeycloakOptionsSetup.SettingKeys.AdminClientSecret,
        KeycloakOptionsSetup.SettingKeys.AppClientId
    };

    /// <summary>Settings store read by the substitute <see cref="ISettingsService"/>.</summary>
    private readonly Dictionary<string, string?> _settings = new();

    private ServiceProvider BuildProvider()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(0);
                return Task.FromResult(_settings.TryGetValue(key, out var value)
                    ? OperationResult<string>.Ok(value)
                    : OperationResult<string>.NotFound($"Setting '{key}' not found"));
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => settingsService);
        services.AddSingleton<KeycloakOptionsSetup>();
        services.AddSingleton<IConfigureOptions<KeycloakOptions>>(sp => sp.GetRequiredService<KeycloakOptionsSetup>());
        services.AddSingleton<IOptionsChangeTokenSource<KeycloakOptions>>(sp => sp.GetRequiredService<KeycloakOptionsSetup>());
        services.AddOptions<KeycloakOptions>();
        return services.BuildServiceProvider();
    }

    private static SettingChangedEvent Changed(string key) => new() { SettingKey = key, LevelId = Guid.NewGuid() };

    private static string[] ValuesOf(KeycloakOptions o) =>
        new[] { o.PublicBaseUrl, o.SharedRealmName, o.InternalBaseUrl, o.AdminClientId, o.AdminClientSecret, o.AppClientId };

    /// <summary>Property 1: each Keycloak setting maps to its option; a missing setting maps to empty.</summary>
    [Property(MaxTest = 50)]
    public Property Configure_MapsEachSettingToItsOption()
    {
        var valueOrMissing = Gen.OneOf(
            Gen.Constant<string?>(null),
            Arb.Generate<NonEmptyString>().Select(s => (string?)s.Get));

        return Prop.ForAll(Arb.From(Gen.ArrayOf(Keys.Length, valueOrMissing)), values =>
        {
            _settings.Clear();
            for (var i = 0; i < Keys.Length; i++)
            {
                if (values[i] is not null)
                {
                    _settings[Keys[i]] = values[i];
                }
            }

            using var provider = BuildProvider();
            var options = new KeycloakOptions();
            provider.GetRequiredService<KeycloakOptionsSetup>().Configure(options);

            var expected = values.Select(v => v ?? string.Empty).ToArray();
            return ValuesOf(options).SequenceEqual(expected).ToProperty();
        });
    }

    [Fact]
    public async Task HandleAsync_KeycloakSettingChanged_FiresChangeToken()
    {
        using var provider = BuildProvider();
        var setup = provider.GetRequiredService<KeycloakOptionsSetup>();
        var token = setup.GetChangeToken();

        await setup.HandleAsync(Changed(KeycloakOptionsSetup.SettingKeys.SharedRealmName));

        token.HasChanged.Should().BeTrue();
        setup.GetChangeToken().HasChanged.Should().BeFalse("a new token is issued after a change");
    }

    [Fact]
    public async Task HandleAsync_OtherSettingChanged_DoesNotFireChangeToken()
    {
        using var provider = BuildProvider();
        var setup = provider.GetRequiredService<KeycloakOptionsSetup>();
        var token = setup.GetChangeToken();

        await setup.HandleAsync(Changed("app.identity.name"));

        token.HasChanged.Should().BeFalse();
    }

    [Fact]
    public async Task OptionsMonitor_AfterKeycloakSettingChange_ReflectsNewValue()
    {
        _settings[KeycloakOptionsSetup.SettingKeys.SharedRealmName] = "before";
        using var provider = BuildProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<KeycloakOptions>>();
        monitor.CurrentValue.SharedRealmName.Should().Be("before");

        _settings[KeycloakOptionsSetup.SettingKeys.SharedRealmName] = "after";
        await provider.GetRequiredService<KeycloakOptionsSetup>().HandleAsync(
            Changed(KeycloakOptionsSetup.SettingKeys.SharedRealmName));

        monitor.CurrentValue.SharedRealmName.Should().Be("after");
    }
}
