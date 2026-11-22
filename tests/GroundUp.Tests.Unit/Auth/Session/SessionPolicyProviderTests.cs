using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.Session;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Session;

public sealed class SessionPolicyProviderTests : IDisposable
{
    private static readonly Guid SystemLevelId = Guid.NewGuid();
    private static readonly Guid TenantLevelId = Guid.NewGuid();

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ISettingsAdminService _admin = Substitute.For<ISettingsAdminService>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    // (key, levelId, scopeId) -> value
    private readonly Dictionary<(string, Guid, Guid?), int> _values = new();

    public SessionPolicyProviderTests()
    {
        _admin.GetAllLevelsAsync(Arg.Any<CancellationToken>()).Returns(
            OperationResult<IReadOnlyList<SettingLevelDto>>.Ok(new List<SettingLevelDto>
            {
                new(SystemLevelId, "System", null, null, 0),
                new(TenantLevelId, "Tenant", null, SystemLevelId, 1)
            }));

        // Simulates cascade resolution: first level in the chain with a value wins, else the default.
        _settings.GetAsync<int>(Arg.Any<string>(), Arg.Any<IReadOnlyList<SettingScopeEntry>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(0);
                var chain = call.ArgAt<IReadOnlyList<SettingScopeEntry>>(1);
                foreach (var entry in chain)
                {
                    if (_values.TryGetValue((key, entry.LevelId, entry.ScopeId), out var v))
                    {
                        return Task.FromResult(OperationResult<int>.Ok(v));
                    }
                }

                var def = key == SessionSettingKeys.TokenLifetimeMinutes
                    ? SessionSettingKeys.TokenLifetimeDefault
                    : SessionSettingKeys.AbsoluteLifetimeDefault;
                return Task.FromResult(OperationResult<int>.Ok(def));
            });
    }

    private SessionPolicyProvider CreateSut(int fallbackToken = 15, int fallbackAbsolute = 90) =>
        new(_settings, _admin, _cache,
            Options.Create(new AuthOptions
            {
                TokenExpirationMinutes = fallbackToken,
                AbsoluteSessionLifetimeMinutes = fallbackAbsolute
            }),
            NullLogger<SessionPolicyProvider>.Instance);

    private void SetSystem(string key, int value) => _values[(key, SystemLevelId, null)] = value;
    private void SetTenant(string key, Guid tenantId, int value) => _values[(key, TenantLevelId, tenantId)] = value;

    [Fact]
    public async Task GetSystemPolicyAsync_NothingSet_ReturnsSettingDefaults()
    {
        var policy = await CreateSut().GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(SessionSettingKeys.TokenLifetimeDefault));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(480));
    }

    [Fact]
    public async Task GetSystemPolicyAsync_SystemValuesSet_ReturnsThem()
    {
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 30);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 240);

        var policy = await CreateSut().GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(30));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(240));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_TenantValueShorter_TenantWins()
    {
        var tenant = Guid.NewGuid();
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 60);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 480);
        SetTenant(SessionSettingKeys.TokenLifetimeMinutes, tenant, 20);
        SetTenant(SessionSettingKeys.AbsoluteLifetimeMinutes, tenant, 120);

        var policy = await CreateSut().GetPolicyForTenantAsync(tenant);

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(20));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(120));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_TenantValueLongerThanSystem_ClampedToSystem()
    {
        var tenant = Guid.NewGuid();
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 30);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 240);
        SetTenant(SessionSettingKeys.TokenLifetimeMinutes, tenant, 90);
        SetTenant(SessionSettingKeys.AbsoluteLifetimeMinutes, tenant, 1000);

        var policy = await CreateSut().GetPolicyForTenantAsync(tenant);

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(30));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(240));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_NoTenantValue_InheritsSystem()
    {
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 45);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 300);

        var policy = await CreateSut().GetPolicyForTenantAsync(Guid.NewGuid());

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(45));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(300));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_OtherTenantHasValue_IsNotAffected()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        SetTenant(SessionSettingKeys.TokenLifetimeMinutes, tenantA, 10);

        var policyB = await CreateSut().GetPolicyForTenantAsync(tenantB);

        policyB.TokenLifetime.Should().Be(TimeSpan.FromMinutes(SessionSettingKeys.TokenLifetimeDefault));
    }

    [Fact]
    public async Task GetSystemPolicyAsync_AbsoluteShorterThanToken_UsesAbsoluteForBoth()
    {
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 60);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 20);

        var policy = await CreateSut().GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(20));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_TenantTokenLongerThanTenantAbsolute_TokenReducedToAbsolute()
    {
        var tenant = Guid.NewGuid();
        SetSystem(SessionSettingKeys.TokenLifetimeMinutes, 60);
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 480);
        SetTenant(SessionSettingKeys.AbsoluteLifetimeMinutes, tenant, 30);

        var policy = await CreateSut().GetPolicyForTenantAsync(tenant);

        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(30));
        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task GetSystemPolicyAsync_SettingsThrow_ReturnsAuthOptionsFallback()
    {
        _settings.GetAsync<int>(Arg.Any<string>(), Arg.Any<IReadOnlyList<SettingScopeEntry>>(), Arg.Any<CancellationToken>())
            .Returns<Task<OperationResult<int>>>(_ => throw new InvalidOperationException("db down"));

        var policy = await CreateSut(fallbackToken: 15, fallbackAbsolute: 90).GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(15));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public async Task GetSystemPolicyAsync_SystemLevelMissing_ReturnsAuthOptionsFallback()
    {
        _admin.GetAllLevelsAsync(Arg.Any<CancellationToken>()).Returns(
            OperationResult<IReadOnlyList<SettingLevelDto>>.Ok(new List<SettingLevelDto>()));

        var policy = await CreateSut(fallbackToken: 15, fallbackAbsolute: 90).GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(15));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public async Task GetSystemPolicyAsync_OneKeyFailsToResolve_UsesFallbackForThatKeyOnly()
    {
        SetSystem(SessionSettingKeys.AbsoluteLifetimeMinutes, 200);
        _settings.GetAsync<int>(SessionSettingKeys.TokenLifetimeMinutes, Arg.Any<IReadOnlyList<SettingScopeEntry>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<int>.NotFound("missing"));

        var policy = await CreateSut(fallbackToken: 15, fallbackAbsolute: 90).GetSystemPolicyAsync();

        policy.TokenLifetime.Should().Be(TimeSpan.FromMinutes(15));
        policy.AbsoluteLifetime.Should().Be(TimeSpan.FromMinutes(200));
    }

    [Fact]
    public async Task GetPolicyForTenantAsync_CalledTwice_LooksUpLevelsOnce()
    {
        var sut = CreateSut();

        await sut.GetPolicyForTenantAsync(Guid.NewGuid());
        await sut.GetPolicyForTenantAsync(Guid.NewGuid());

        await _admin.Received(1).GetAllLevelsAsync(Arg.Any<CancellationToken>());
    }

    public void Dispose() => _cache.Dispose();
}
