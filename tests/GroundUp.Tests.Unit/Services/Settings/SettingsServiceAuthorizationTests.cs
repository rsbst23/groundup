using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Events;
using GroundUp.Repositories.Settings;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Repositories;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

public sealed class SettingsServiceAuthorizationTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();
    private readonly ISettingsAuthorizer _authorizer = Substitute.For<ISettingsAuthorizer>();

    private SettingsService CreateService(TestSettingsDbContext context)
        => new(new SettingsRepository(context, SqliteUniqueConstraintDetector.Instance), _fixture.EventBus, _fixture.ScopeChainProvider, _fixture.MemoryCache,
            _fixture.CacheOptions, _fixture.CacheKeyTracker, _authorizer);

    private void Decide(SettingsAuthorizationResult result)
    {
        _authorizer.AuthorizeWriteAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs(result);
        _authorizer.AuthorizeDeleteAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs(result);
    }

    [Fact]
    public async Task SetAsync_Forbidden_ReturnsForbiddenAndWritesNothing()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        Decide(SettingsAuthorizationResult.Forbidden);

        var result = await CreateService(context).SetAsync("Guarded", "value", levelId, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        (await context.Set<SettingValue>().CountAsync()).Should().Be(0);
        await _fixture.EventBus.DidNotReceiveWithAnyArgs().PublishAsync<SettingChangedEvent>(default!, default);
    }

    [Fact]
    public async Task SetAsync_Unauthenticated_Returns401()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        Decide(SettingsAuthorizationResult.Unauthenticated);

        var result = await CreateService(context).SetAsync("Guarded", "value", levelId, null);

        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task SetAsync_Forbidden_UnknownKeyAlsoReturnsForbiddenNotNotFound()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        Decide(SettingsAuthorizationResult.Forbidden);

        var result = await CreateService(context).SetAsync("does.not.exist", "value", levelId, null);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task SetAsync_Allowed_PassesKeyLevelNameAndScopeToAuthorizer()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        var tenantId = Guid.NewGuid();
        Decide(SettingsAuthorizationResult.Allowed);

        var result = await CreateService(context).SetAsync("Guarded", "value", levelId, tenantId);

        result.Success.Should().BeTrue();
        await _authorizer.Received(1).AuthorizeWriteAsync("Guarded", levelId, "Tenant", tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteValueAsync_Forbidden_ReturnsForbiddenAndKeepsValue()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        var valueId = await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "keep");
        Decide(SettingsAuthorizationResult.Forbidden);

        var result = await CreateService(context).DeleteValueAsync(valueId);

        result.StatusCode.Should().Be(403);
        (await context.Set<SettingValue>().AnyAsync(v => v.Id == valueId)).Should().BeTrue();
        await _fixture.EventBus.DidNotReceiveWithAnyArgs().PublishAsync<SettingChangedEvent>(default!, default);
    }

    [Fact]
    public async Task DeleteValueAsync_Unauthenticated_Returns401()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "System");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        var valueId = await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, "keep");
        Decide(SettingsAuthorizationResult.Unauthenticated);

        var result = await CreateService(context).DeleteValueAsync(valueId);

        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task DeleteValueAsync_Allowed_PassesStoredKeyLevelAndScopeToAuthorizer()
    {
        using var context = _fixture.CreateContext();
        var levelId = await SettingsTestFixture.SeedLevelAsync(context, "Tenant");
        var definition = await SettingsTestFixture.SeedDefinitionAsync(context, key: "Guarded", allowedLevelIds: levelId);
        var tenantId = Guid.NewGuid();
        var valueId = await SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, tenantId, "x");
        Decide(SettingsAuthorizationResult.Allowed);

        var result = await CreateService(context).DeleteValueAsync(valueId);

        result.Success.Should().BeTrue();
        await _authorizer.Received(1).AuthorizeDeleteAsync("Guarded", levelId, "Tenant", tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteValueAsync_NotFound_DoesNotCallAuthorizer()
    {
        using var context = _fixture.CreateContext();

        var result = await CreateService(context).DeleteValueAsync(Guid.NewGuid());

        result.StatusCode.Should().Be(404);
        await _authorizer.DidNotReceiveWithAnyArgs().AuthorizeDeleteAsync(default!, default, default, default, default);
    }

    public void Dispose() => _fixture.Dispose();
}
