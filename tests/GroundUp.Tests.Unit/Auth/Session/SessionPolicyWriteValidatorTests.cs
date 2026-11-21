using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Services.Session;
using GroundUp.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Session;

public sealed class SessionPolicyWriteValidatorTests
{
    private static readonly Guid LevelId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly ISessionPolicyProvider _provider = Substitute.For<ISessionPolicyProvider>();
    private readonly SessionPolicyWriteValidator _sut;

    public SessionPolicyWriteValidatorTests()
    {
        // System: 60 / 480. The tenant has no override of its own yet.
        var system = new SessionPolicy(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(480));
        _provider.GetSystemPolicyAsync(Arg.Any<CancellationToken>()).Returns(system);
        _provider.GetPolicyForTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(system);

        var services = new ServiceCollection();
        services.AddSingleton(_provider);
        _sut = new SessionPolicyWriteValidator(services.BuildServiceProvider());
    }

    private static SettingWriteContext Ctx(string key, string value, string level, Guid? scope) =>
        new(key, value, LevelId, level, scope);

    [Fact]
    public async Task ValidateAsync_UnrelatedKey_ReturnsNull()
    {
        var result = await _sut.ValidateAsync(Ctx("other.key", "9999", "Tenant", TenantId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_EmptyValue_ReturnsNull()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "", "Tenant", TenantId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_NotANumber_ReturnsError()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "abc", "System", null));

        result.Should().Contain("whole number");
    }

    [Fact]
    public async Task ValidateAsync_UnknownLevel_ReturnsNull()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "5", "User", Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(SessionSettingKeys.TokenLifetimeMinutes, "61")]
    [InlineData(SessionSettingKeys.AbsoluteLifetimeMinutes, "481")]
    public async Task ValidateAsync_TenantValueAboveSystem_ReturnsError(string key, string value)
    {
        var result = await _sut.ValidateAsync(Ctx(key, value, "Tenant", TenantId));

        result.Should().Contain("cannot be longer than the System value");
    }

    [Theory]
    [InlineData(SessionSettingKeys.TokenLifetimeMinutes, "60")]
    [InlineData(SessionSettingKeys.TokenLifetimeMinutes, "10")]
    [InlineData(SessionSettingKeys.AbsoluteLifetimeMinutes, "480")]
    [InlineData(SessionSettingKeys.AbsoluteLifetimeMinutes, "100")]
    public async Task ValidateAsync_TenantValueEqualOrBelowSystem_ReturnsNull(string key, string value)
    {
        var result = await _sut.ValidateAsync(Ctx(key, value, "Tenant", TenantId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_TenantWithoutScope_ReturnsError()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "10", "Tenant", null));

        result.Should().Contain("must specify the tenant");
    }

    [Fact]
    public async Task ValidateAsync_TenantTokenLongerThanTenantAbsolute_ReturnsError()
    {
        // The tenant already limits the absolute lifetime to 30 minutes.
        _provider.GetPolicyForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)));

        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "45", "Tenant", TenantId));

        result.Should().Contain("cannot be longer than the absolute session lifetime");
    }

    [Fact]
    public async Task ValidateAsync_TenantAbsoluteShorterThanTenantToken_ReturnsError()
    {
        _provider.GetPolicyForTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new SessionPolicy(TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(480)));

        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.AbsoluteLifetimeMinutes, "30", "Tenant", TenantId));

        result.Should().Contain("cannot be shorter than the token lifetime");
    }

    [Fact]
    public async Task ValidateAsync_SystemTokenAboveAbsolute_ReturnsError()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "500", "System", null));

        result.Should().Contain("cannot be longer than the absolute session lifetime");
    }

    [Fact]
    public async Task ValidateAsync_SystemAbsoluteBelowToken_ReturnsError()
    {
        var result = await _sut.ValidateAsync(Ctx(SessionSettingKeys.AbsoluteLifetimeMinutes, "30", "System", null));

        result.Should().Contain("cannot be shorter than the token lifetime");
    }

    [Fact]
    public async Task ValidateAsync_SystemValidValues_ReturnsNull()
    {
        (await _sut.ValidateAsync(Ctx(SessionSettingKeys.TokenLifetimeMinutes, "30", "System", null))).Should().BeNull();
        (await _sut.ValidateAsync(Ctx(SessionSettingKeys.AbsoluteLifetimeMinutes, "60", "System", null))).Should().BeNull();
    }
}
