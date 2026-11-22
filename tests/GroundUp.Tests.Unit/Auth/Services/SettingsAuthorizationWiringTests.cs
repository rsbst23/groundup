using System.Reflection;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Authorization;
using GroundUp.Auth.Services.Settings;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

public sealed class SettingsAuthorizationWiringTests
{
    // ── Attributes ────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(ISettingsService), "GetAllForScopeAsync")]
    [InlineData(typeof(ISettingsService), "GetGroupAsync")]
    [InlineData(typeof(ISettingsAdminService), "GetAllGroupsAsync")]
    [InlineData(typeof(ISettingsAdminService), "GetAllDefinitionsAsync")]
    [InlineData(typeof(ISettingsAdminService), "GetDefinitionByIdAsync")]
    public void ListingReads_RequireSettingsRead(Type service, string method)
    {
        foreach (var m in service.GetMethods().Where(m => m.Name == method))
        {
            var attribute = m.GetCustomAttribute<RequiresPermissionAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(new[] { "settings.read" }, attribute!.Permissions);
        }
    }

    [Theory]
    [InlineData("CreateLevelAsync")]
    [InlineData("UpdateLevelAsync")]
    [InlineData("DeleteLevelAsync")]
    [InlineData("CreateGroupAsync")]
    [InlineData("UpdateGroupAsync")]
    [InlineData("DeleteGroupAsync")]
    [InlineData("CreateDefinitionAsync")]
    [InlineData("UpdateDefinitionAsync")]
    [InlineData("DeleteDefinitionAsync")]
    public void AdminWrites_RequireSuperAdmin(string method)
    {
        var m = typeof(ISettingsAdminService).GetMethod(method)!;
        var attribute = m.GetCustomAttribute<RequiresRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(new[] { "SuperAdmin" }, attribute!.Roles);
    }

    [Fact]
    public void InternalReadsAndEnsure_HaveNoAttributes()
    {
        var open = new (Type Service, string Method)[]
        {
            (typeof(ISettingsService), "GetAsync"),
            (typeof(ISettingsService), "GetTypedValueAsync"),
            (typeof(ISettingsService), "EnsureDefinitionAsync"),
            (typeof(ISettingsAdminService), "GetAllLevelsAsync"),
        };

        foreach (var (service, method) in open)
        {
            foreach (var m in service.GetMethods().Where(m => m.Name == method))
            {
                Assert.Null(m.GetCustomAttribute<RequiresPermissionAttribute>());
                Assert.Null(m.GetCustomAttribute<RequiresRoleAttribute>());
            }
        }
    }

    // ── Startup validator ─────────────────────────────────────────

    private static IServiceProvider BuildProvider(bool wrapSettings, bool wrapAdmin, bool registerSettings = true)
    {
        var permissions = Substitute.For<IPermissionService>();
        var user = Substitute.For<ICurrentUser>();
        var services = new ServiceCollection();

        if (registerSettings)
        {
            services.AddScoped(_ => wrapSettings
                ? AuthorizationInterceptor<ISettingsService>.Create(Substitute.For<ISettingsService>(), permissions, user)
                : Substitute.For<ISettingsService>());
            services.AddScoped(_ => wrapAdmin
                ? AuthorizationInterceptor<ISettingsAdminService>.Create(Substitute.For<ISettingsAdminService>(), permissions, user)
                : Substitute.For<ISettingsAdminService>());
        }

        return services.BuildServiceProvider();
    }

    private static Task Start(IServiceProvider provider)
        => new SettingsAuthorizationStartupValidator(provider.GetRequiredService<IServiceScopeFactory>())
            .StartAsync(CancellationToken.None);

    [Fact]
    public async Task Validator_BothServicesWrapped_Starts()
    {
        await Start(BuildProvider(wrapSettings: true, wrapAdmin: true));
    }

    [Fact]
    public async Task Validator_SettingsServiceNotWrapped_ThrowsNamingTheRegistration()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Start(BuildProvider(wrapSettings: false, wrapAdmin: true)));

        Assert.Contains("ISettingsService", ex.Message);
        Assert.Contains("AddAuthorized", ex.Message);
    }

    [Fact]
    public async Task Validator_AdminServiceNotWrapped_ThrowsNamingTheRegistration()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Start(BuildProvider(wrapSettings: true, wrapAdmin: false)));

        Assert.Contains("ISettingsAdminService", ex.Message);
        Assert.Contains("AddAuthorized", ex.Message);
    }

    [Fact]
    public async Task Validator_SettingsModuleNotRegistered_Starts()
    {
        await Start(BuildProvider(wrapSettings: false, wrapAdmin: false, registerSettings: false));
    }

    [Fact]
    public void AddGroundUpAuth_RegistersTheValidator()
    {
        var services = new ServiceCollection();

        services.AddGroundUpAuth(_ => { });

        Assert.Contains(services, d => d.ImplementationType == typeof(SettingsAuthorizationStartupValidator));
    }
}
