using GroundUp.Auth.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsAuthorizationDefaultsTests
{
    [Fact]
    public async Task AllowAllSettingsAuthorizer_Write_ReturnsAllowed()
    {
        var sut = new AllowAllSettingsAuthorizer();

        var result = await sut.AuthorizeWriteAsync("any.key", Guid.NewGuid(), "System", null);

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public async Task AllowAllSettingsAuthorizer_Delete_ReturnsAllowed()
    {
        var sut = new AllowAllSettingsAuthorizer();

        var result = await sut.AuthorizeDeleteAsync("any.key", Guid.NewGuid(), "Tenant", Guid.NewGuid());

        Assert.Equal(SettingsAuthorizationResult.Allowed, result);
    }

    [Fact]
    public void AddGroundUpSettings_RegistersAllowAllAuthorizerByDefault()
    {
        var services = new ServiceCollection();

        services.AddGroundUpSettings();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISettingsAuthorizer));
        Assert.Equal(typeof(AllowAllSettingsAuthorizer), descriptor.ImplementationType);
    }

    [Fact]
    public void AddGroundUpSettings_DoesNotReplaceAuthorizerRegisteredEarlier()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISettingsAuthorizer, StubAuthorizer>();

        services.AddGroundUpSettings();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISettingsAuthorizer));
        Assert.Equal(typeof(StubAuthorizer), descriptor.ImplementationType);
    }

    [Theory]
    [InlineData("app.identity.name")]
    [InlineData("auth.application.default-domain")]
    [InlineData("auth.keycloak.public-base-url")]
    [InlineData("auth.keycloak.internal-base-url")]
    [InlineData("auth.keycloak.shared-realm-name")]
    [InlineData("auth.keycloak.admin-client-id")]
    [InlineData("auth.keycloak.admin-client-secret")]
    public void SettingsSetupAllowlist_ContainsWizardKeys(string key)
    {
        Assert.True(SettingsSetupAllowlist.Contains(key));
    }

    [Theory]
    [InlineData("auth.session.token-lifetime-minutes")]
    [InlineData("auth.session.absolute-lifetime-minutes")]
    [InlineData("auth.application.default-tenant-slug")]
    [InlineData("AUTH.APPLICATION.DEFAULT-DOMAIN")]
    [InlineData("")]
    public void SettingsSetupAllowlist_DoesNotContainOtherKeys(string key)
    {
        Assert.False(SettingsSetupAllowlist.Contains(key));
    }

    [Fact]
    public void PermissionKeysAuth_HasSettingsReadAndManageTenant_ButNotRemovedKeys()
    {
        var keys = PermissionKeysAuth.All.Select(p => p.Key).ToList();

        Assert.Contains("settings.read", keys);
        Assert.Contains("settings.manage.tenant", keys);
        Assert.DoesNotContain("settings.write", keys);
        Assert.DoesNotContain("settings.delete", keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    private sealed class StubAuthorizer : ISettingsAuthorizer
    {
        public Task<SettingsAuthorizationResult> AuthorizeWriteAsync(
            string key, Guid levelId, string? levelName, Guid? scopeId, CancellationToken cancellationToken = default)
            => Task.FromResult(SettingsAuthorizationResult.Forbidden);

        public Task<SettingsAuthorizationResult> AuthorizeDeleteAsync(
            string key, Guid levelId, string? levelName, Guid? scopeId, CancellationToken cancellationToken = default)
            => Task.FromResult(SettingsAuthorizationResult.Forbidden);
    }
}
