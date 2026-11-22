using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Postgres.Seeders;
using GroundUp.Auth.Services.Settings;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Data.Postgres;
using GroundUp.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Settings authorization end to end (GU-77): real JWTs, the real permission service, the
/// authorization proxy and <see cref="SettingsAuthorizer"/>, through the settings HTTP endpoints.
/// <para>
/// Uses the session timeout key because it allows System and Tenant values. Each test removes
/// the values it wrote, so other tests in the collection see the defaults.
/// </para>
/// </summary>
[Collection("AuthFlow")]
public sealed class SettingsAuthorizationIntegrationTests : AuthFlowTestBase, IAsyncLifetime
{
    private const string Key = SessionSettingKeys.TokenLifetimeMinutes;

    private readonly List<Guid> _writtenValueIds = new();
    private Guid _systemLevelId;
    private Guid _tenantLevelId;

    public SettingsAuthorizationIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    public async Task InitializeAsync()
    {
        // Hosted services (and so the seeders) do not run in this fixture.
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GroundUpDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        await new DefaultSettingLevelSeeder(db).SeedAsync();
        await new DefaultAuthSettingsSeeder(settings, NullLogger<DefaultAuthSettingsSeeder>.Instance).SeedAsync();

        var levels = await db.Set<SettingLevel>().AsNoTracking().ToListAsync();
        _systemLevelId = levels.Single(l => l.Name == "System").Id;
        _tenantLevelId = levels.Single(l => l.Name == "Tenant").Id;
    }

    public async Task DisposeAsync()
    {
        using var scope = Fixture.Services.CreateScope();
        var settings = CreateUnrestrictedSettings(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<GroundUpDbContext>();

        foreach (var id in Enumerable.Reverse(_writtenValueIds).Distinct())
        {
            if (await db.Set<SettingValue>().AnyAsync(v => v.Id == id))
            {
                await settings.DeleteValueAsync(id);
            }
        }
    }

    // ── Value writes ──────────────────────────────────────────────

    [Fact]
    public async Task PutValue_NoUser_Returns401()
    {
        var tenantId = await SeedTenantAsync("SA anon", Slug("anon"));

        var response = await Client.SendAsync(Put(token: null, _tenantLevelId, tenantId, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutValue_MemberWithoutRole_Returns403()
    {
        var (token, tenantId) = await SeedMemberAsync("plain");

        var response = await Client.SendAsync(Put(token, _tenantLevelId, tenantId, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutValue_TenantAdminOwnTenant_Succeeds()
    {
        var (token, tenantId) = await SeedTenantAdminAsync("own");

        var response = await Client.SendAsync(Put(token, _tenantLevelId, tenantId, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _writtenValueIds.Add(await ReadValueIdAsync(response));
    }

    [Fact]
    public async Task PutValue_TenantAdminOtherTenant_Returns403()
    {
        var (token, _) = await SeedTenantAdminAsync("a");
        var otherTenantId = await SeedTenantAsync("SA other", Slug("other"));

        var response = await Client.SendAsync(Put(token, _tenantLevelId, otherTenantId, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountValuesAsync(otherTenantId)).Should().Be(0);
    }

    [Fact]
    public async Task PutValue_TenantAdminSystemLevel_Returns403()
    {
        var (token, _) = await SeedTenantAdminAsync("sys");

        var response = await Client.SendAsync(Put(token, _systemLevelId, scopeId: null, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutValue_SuperAdminSystemLevel_Succeeds()
    {
        var token = await SeedSuperAdminAsync();

        // 60 is the default, so other tests in the collection are not affected even before cleanup
        var response = await Client.SendAsync(Put(token, _systemLevelId, scopeId: null, "60"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _writtenValueIds.Add(await ReadValueIdAsync(response));
    }

    [Fact]
    public async Task PutValue_SuperAdminAnyTenant_Succeeds()
    {
        var token = await SeedSuperAdminAsync();
        var tenantId = await SeedTenantAsync("SA any", Slug("any"));

        var response = await Client.SendAsync(Put(token, _tenantLevelId, tenantId, "30"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _writtenValueIds.Add(await ReadValueIdAsync(response));
    }

    // ── Value deletes ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteValue_TenantAdminOwnTenant_Succeeds()
    {
        var (token, tenantId) = await SeedTenantAdminAsync("del");
        var valueId = await WriteUnrestrictedAsync(_tenantLevelId, tenantId, "30");

        var response = await Client.SendAsync(Bearer(HttpMethod.Delete, $"/api/settings/values/{valueId}", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountValuesAsync(tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteValue_TenantAdminOtherTenant_Returns403AndKeepsValue()
    {
        var (token, _) = await SeedTenantAdminAsync("delother");
        var otherTenantId = await SeedTenantAsync("SA delother", Slug("delother"));
        var valueId = await WriteUnrestrictedAsync(_tenantLevelId, otherTenantId, "30");

        var response = await Client.SendAsync(Bearer(HttpMethod.Delete, $"/api/settings/values/{valueId}", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountValuesAsync(otherTenantId)).Should().Be(1);
    }

    // ── Listing reads (settings.read through the proxy) ───────────

    [Fact]
    public async Task GetAll_TenantAdmin_SucceedsThroughAdminBypass()
    {
        var (token, _) = await SeedTenantAdminAsync("read");

        var response = await Client.SendAsync(Bearer(HttpMethod.Get, "/api/settings", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_MemberWithoutRole_Returns403()
    {
        var (token, _) = await SeedMemberAsync("noread");

        var response = await Client.SendAsync(Bearer(HttpMethod.Get, "/api/settings", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetByKey_NoUser_StaysOpen()
    {
        var response = await Client.GetAsync($"/api/settings/{Key}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Admin service (SuperAdmin only) ───────────────────────────

    [Fact]
    public async Task CreateGroup_TenantAdmin_Returns403()
    {
        var (token, _) = await SeedTenantAdminAsync("grp");

        var response = await Client.SendAsync(CreateGroupRequest(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAndDeleteGroup_SuperAdmin_Succeeds()
    {
        var token = await SeedSuperAdminAsync();

        var create = await Client.SendAsync(CreateGroupRequest(token));
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var groupId = await ReadValueIdAsync(create);

        var delete = await Client.SendAsync(Bearer(HttpMethod.Delete, $"/api/settings/groups/{groupId}", token));
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLevels_NoUser_StaysOpen()
    {
        var response = await Client.GetAsync("/api/settings/levels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Setup exception and wiring ────────────────────────────────

    [Fact]
    public async Task Authorizer_SetupIncomplete_AllowsOnlyAllowlistedSystemKeysWithoutUser()
    {
        // The fixture never completes setup, so this is the first-run state the wizard sees.
        using var scope = Fixture.Services.CreateScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IBootstrapStateService>();
        (await bootstrap.IsCompleteAsync()).Should().BeFalse();

        var authorizer = scope.ServiceProvider.GetRequiredService<ISettingsAuthorizer>();
        authorizer.Should().BeOfType<SettingsAuthorizer>();

        (await authorizer.AuthorizeWriteAsync("auth.keycloak.public-base-url", _systemLevelId, "System", null))
            .Should().Be(SettingsAuthorizationResult.Allowed);
        (await authorizer.AuthorizeWriteAsync(Key, _systemLevelId, "System", null))
            .Should().Be(SettingsAuthorizationResult.Unauthenticated);
        (await authorizer.AuthorizeDeleteAsync("auth.keycloak.public-base-url", _systemLevelId, "System", null))
            .Should().Be(SettingsAuthorizationResult.Unauthenticated);
    }

    [Fact]
    public async Task SampleApp_SettingsServicesAreWrappedAndStartupCheckPasses()
    {
        using var scope = Fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISettingsService>().Should().BeAssignableTo<DispatchProxy>();
        scope.ServiceProvider.GetRequiredService<ISettingsAdminService>().Should().BeAssignableTo<DispatchProxy>();

        var validator = new SettingsAuthorizationStartupValidator(
            Fixture.Services.GetRequiredService<IServiceScopeFactory>());

        await validator.Invoking(v => v.StartAsync(CancellationToken.None)).Should().NotThrowAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static string Slug(string label) => $"sa-{label}-{Guid.NewGuid().ToString("N")[..8]}";

    private HttpRequestMessage Bearer(HttpMethod method, string url, string token)
        => CreateBearerRequest(method, url, token);

    private HttpRequestMessage Put(string? token, Guid levelId, Guid? scopeId, string value)
    {
        var request = token is null
            ? new HttpRequestMessage(HttpMethod.Put, $"/api/settings/{Key}")
            : CreateBearerRequest(HttpMethod.Put, $"/api/settings/{Key}", token);
        request.Content = JsonContent.Create(new SetSettingValueDto(value, levelId, scopeId));
        return request;
    }

    private HttpRequestMessage CreateGroupRequest(string token)
    {
        var request = CreateBearerRequest(HttpMethod.Post, "/api/settings/groups", token);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        request.Content = JsonContent.Create(new CreateSettingGroupDto($"sa.group.{suffix}", $"SA group {suffix}", null, null, 0));
        return request;
    }

    private static async Task<Guid> ReadValueIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<int> CountValuesAsync(Guid tenantId)
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GroundUpDbContext>();
        return await db.Set<SettingValue>().CountAsync(v => v.LevelId == _tenantLevelId && v.ScopeId == tenantId);
    }

    private static ISettingsService CreateUnrestrictedSettings(IServiceProvider provider)
        => ActivatorUtilities.CreateInstance<SettingsService>(provider, new AllowAllSettingsAuthorizer());

    private async Task<Guid> WriteUnrestrictedAsync(Guid levelId, Guid? scopeId, string value)
    {
        using var scope = Fixture.Services.CreateScope();
        var result = await CreateUnrestrictedSettings(scope.ServiceProvider).SetAsync(Key, value, levelId, scopeId);
        result.Success.Should().BeTrue(result.Message);
        _writtenValueIds.Add(result.Data!.Id);
        return result.Data.Id;
    }

    private async Task<(string Token, Guid TenantId)> SeedMemberAsync(string label)
    {
        var slug = Slug(label);
        var tenantId = await SeedTenantAsync($"SA {slug}", slug);
        var userId = await SeedUserAsync($"sub-{slug}", $"{slug}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        return (Fixture.GenerateTestToken(userId, tenantId, $"{slug}@example.com"), tenantId);
    }

    private async Task<(string Token, Guid TenantId)> SeedTenantAdminAsync(string label)
    {
        var slug = Slug(label);
        var tenantId = await SeedTenantAsync($"SA {slug}", slug);
        var userId = await SeedUserAsync($"sub-{slug}", $"{slug}@example.com");
        await SeedMembershipAsync(userId, tenantId);
        var roleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, roleId, tenantId);
        return (Fixture.GenerateTestToken(userId, tenantId, $"{slug}@example.com"), tenantId);
    }

    private async Task<string> SeedSuperAdminAsync()
    {
        var slug = Slug("super");
        var tenantId = await SeedTenantAsync($"SA {slug}", slug);
        var userId = await SeedUserAsync($"sub-{slug}", $"{slug}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        await using (var context = Fixture.CreateAuthDbContext())
        {
            var role = new Role
            {
                Name = AuthRoleNames.SuperAdmin,
                RoleType = RoleType.System,
                TenantId = tenantId,
                IsSystem = true
            };
            context.Set<Role>().Add(role);
            await context.SaveChangesAsync();
            context.Set<UserRole>().Add(new UserRole { UserId = userId, RoleId = role.Id, TenantId = tenantId });
            await context.SaveChangesAsync();
        }

        return Fixture.GenerateTestToken(userId, tenantId, $"{slug}@example.com");
    }
}
