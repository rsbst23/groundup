using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Data.Postgres.Seeders;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Session;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Data.Postgres;
using GroundUp.Events;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Session;

/// <summary>
/// Real settings service, real seeder, real validator and real provider over SQLite.
/// Proves the components wire together without a dependency cycle and that the rules hold
/// through the settings API (<see cref="ISettingsService.SetAsync"/>).
/// </summary>
public sealed class SessionPolicySettingsEndToEndTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();
    private readonly TestSettingsDbContext _context;
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;
    private readonly Guid _systemLevelId;
    private readonly Guid _tenantLevelId;

    public SessionPolicySettingsEndToEndTests()
    {
        _context = _fixture.CreateContext();
        _systemLevelId = SettingsTestFixture.SeedLevelAsync(_context, "System").GetAwaiter().GetResult();
        _tenantLevelId = SettingsTestFixture.SeedLevelAsync(_context, "Tenant", 1).GetAwaiter().GetResult();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOptions();
        services.Configure<SettingsCacheOptions>(_ => { });
        services.Configure<AuthOptions>(o =>
        {
            o.TokenExpirationMinutes = 15;
            o.AbsoluteSessionLifetimeMinutes = 90;
        });
        services.AddSingleton<SettingsCacheKeyTracker>();
        services.AddScoped<GroundUpDbContext>(_ => _context);
        services.AddScoped<IScopeChainProvider>(_ => Substitute.For<IScopeChainProvider>());
        services.AddScoped<IEventBus, InvalidatingEventBus>();
        services.AddScoped<SettingsCacheInvalidationHandler>();
        services.AddScoped<ISettingsAuthorizer, AllowAllSettingsAuthorizer>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ISettingsAdminService, SettingsAdminService>();
        services.AddScoped<ISessionPolicyProvider, SessionPolicyProvider>();
        services.AddScoped<ISettingWriteValidator, SessionPolicyWriteValidator>();

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();

        // Seed through the real seeder (also proves the definitions are accepted).
        var seeder = new DefaultAuthSettingsSeeder(
            _scope.ServiceProvider.GetRequiredService<ISettingsService>(),
            NullLogger<DefaultAuthSettingsSeeder>.Instance);
        seeder.SeedAsync().GetAwaiter().GetResult();
    }

    private ISettingsService Settings => _scope.ServiceProvider.GetRequiredService<ISettingsService>();
    private ISessionPolicyProvider Provider => _scope.ServiceProvider.GetRequiredService<ISessionPolicyProvider>();

    [Fact]
    public async Task Resolve_AfterSeeding_ReturnsDefaults()
    {
        var policy = await Provider.GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(60));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(480));
    }

    [Fact]
    public async Task SetAsync_SystemValueWithinBounds_AppliesToNextResolution()
    {
        var set = await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "30", _systemLevelId, null);
        var policy = await Provider.GetSystemPolicyAsync();

        set.Success.Should().BeTrue();
        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData(SessionSettingKeys.TokenLifetimeMinutes, "1")]
    [InlineData(SessionSettingKeys.TokenLifetimeMinutes, "121")]
    [InlineData(SessionSettingKeys.AbsoluteLifetimeMinutes, "9")]
    [InlineData(SessionSettingKeys.AbsoluteLifetimeMinutes, "1441")]
    public async Task SetAsync_OutsideBounds_IsRejected(string key, string value)
    {
        var set = await Settings.SetAsync(key, value, _systemLevelId, null);

        set.Success.Should().BeFalse();
        set.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SetAsync_TenantValueAboveSystem_IsRejected()
    {
        await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "30", _systemLevelId, null);

        var set = await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "45", _tenantLevelId, Guid.NewGuid());

        set.Success.Should().BeFalse();
        set.StatusCode.Should().Be(400);
        set.Message.Should().Contain("System value");
    }

    [Fact]
    public async Task SetAsync_TenantValueBelowSystem_AppliesToThatTenantOnly()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "30", _systemLevelId, null);

        var set = await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "10", _tenantLevelId, tenantA);
        var policyA = await Provider.GetPolicyForTenantAsync(tenantA);
        var policyB = await Provider.GetPolicyForTenantAsync(tenantB);

        set.Success.Should().BeTrue();
        policyA.TokenLifetime.Should().Be(TimeSpan.FromMinutes(10));
        policyB.TokenLifetime.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task SetAsync_LoweringSystemBelowExistingTenantValue_TenantIsClampedAtResolution()
    {
        var tenant = Guid.NewGuid();
        await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "40", _systemLevelId, null);
        await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "35", _tenantLevelId, tenant);

        await Settings.SetAsync(SessionSettingKeys.TokenLifetimeMinutes, "20", _systemLevelId, null);
        var policy = await Provider.GetPolicyForTenantAsync(tenant);

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task SetAsync_AbsoluteBelowToken_IsRejected()
    {
        // Defaults are token 60, absolute 480.
        var set = await Settings.SetAsync(SessionSettingKeys.AbsoluteLifetimeMinutes, "30", _systemLevelId, null);

        set.Success.Should().BeFalse();
        set.Message.Should().Contain("shorter than the token lifetime");
    }

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        _context.Dispose();
        _fixture.Dispose();
    }

    /// <summary>Publishes setting changes to the real cache invalidation handler, like the in-process bus does.</summary>
    private sealed class InvalidatingEventBus : IEventBus
    {
        private readonly SettingsCacheInvalidationHandler _handler;

        public InvalidatingEventBus(SettingsCacheInvalidationHandler handler) => _handler = handler;

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            if (@event is SettingChangedEvent changed)
            {
                await _handler.HandleAsync(changed, ct);
            }
        }
    }
}
