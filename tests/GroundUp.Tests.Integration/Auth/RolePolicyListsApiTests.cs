using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-97: sorting, filtering, search and paging on <c>/api/roles</c> and <c>/api/policies</c> against
/// Postgres, with <c>isDefault</c> and <c>policyCount</c> computed in the list query.
/// </summary>
[Collection("AuthFlow")]
public sealed class RolePolicyListsApiTests : AdminApiTestBase
{
    public RolePolicyListsApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Roles: TenantAdmin (built-in), Alpha (Application, "first role", 1 policy, the default) and
    /// Beta (Workspace, "zeta words", 2 policies). Policies: Reader (2 permissions, Alpha and Beta) and
    /// Writer (no permissions, Beta).
    /// </summary>
    private sealed record Seeded(Organization Org, Guid AlphaId, Guid BetaId, Guid ReaderId, Guid WriterId);

    private async Task<Seeded> SeedAsync()
    {
        var org = await SeedOrganizationAsync();
        var alpha = await SeedRoleAsync(org.TenantId, "Alpha");
        var beta = await SeedRoleAsync(org.TenantId, "Beta");
        var membersRead = await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        var rolesRead = await EnsurePermissionAsync(PermissionKeysAuth.RolesRead);

        await using var context = Fixture.CreateAuthDbContext();
        await context.Roles.Where(r => r.Id == alpha)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Description, "first role").SetProperty(r => r.RoleType, RoleType.Application));
        await context.Roles.Where(r => r.Id == beta)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Description, "zeta words").SetProperty(r => r.RoleType, RoleType.Workspace));
        await context.Tenants.Where(t => t.Id == org.TenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.DefaultRoleId, alpha));

        var reader = new Policy { Name = $"Reader {org.Suffix}", Description = "reads things", TenantId = org.TenantId };
        var writer = new Policy { Name = $"Writer {org.Suffix}", TenantId = org.TenantId };
        context.Policies.AddRange(reader, writer);
        context.Set<PolicyPermission>().AddRange(
            new PolicyPermission { Policy = reader, PermissionId = membersRead },
            new PolicyPermission { Policy = reader, PermissionId = rolesRead });
        context.Set<RolePolicy>().AddRange(
            new RolePolicy { RoleId = alpha, Policy = reader },
            new RolePolicy { RoleId = beta, Policy = reader },
            new RolePolicy { RoleId = beta, Policy = writer });
        await context.SaveChangesAsync();
        return new Seeded(org, alpha, beta, reader.Id, writer.Id);
    }

    private async Task<List<JsonElement>> ItemsAsync(Organization org, string path)
    {
        var response = await SendAsync(HttpMethod.Get, path, org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<List<string>> RoleNamesAsync(Organization org, string query)
        => (await ItemsAsync(org, $"/api/roles?{query}")).Select(i => i.GetProperty("name").GetString()!).ToList();

    private async Task<List<string>> PolicyNamesAsync(Seeded s, string query)
        => (await ItemsAsync(s.Org, $"/api/policies?{query}"))
            .Select(i => i.GetProperty("name").GetString()!.Split(' ')[0]).ToList();

    // ── Roles ───────────────────────────────────────────────

    [Fact]
    public async Task Roles_NoSort_ByNameWithDefaultAndPolicyCount()
    {
        var s = await SeedAsync();

        var items = await ItemsAsync(s.Org, "/api/roles");

        items.Select(i => i.GetProperty("name").GetString()).Should().Equal("Alpha", "Beta", AuthRoleNames.TenantAdmin);
        items.Select(i => i.GetProperty("isDefault").GetBoolean()).Should().Equal(true, false, false);
        items.Select(i => i.GetProperty("policyCount").GetInt32()).Should().Equal(1, 2, 0);
    }

    [Fact]
    public async Task Roles_SortByNameTypeAndPolicyCount()
    {
        var s = await SeedAsync();

        (await RoleNamesAsync(s.Org, "sortBy=name%20desc")).Should().Equal(AuthRoleNames.TenantAdmin, "Beta", "Alpha");
        // System (0), Application (1), Workspace (2)
        (await RoleNamesAsync(s.Org, "sortBy=roleType")).Should().Equal(AuthRoleNames.TenantAdmin, "Alpha", "Beta");
        (await RoleNamesAsync(s.Org, "sortBy=policyCount%20desc")).Should().Equal("Beta", "Alpha", AuthRoleNames.TenantAdmin);
        (await RoleNamesAsync(s.Org, "sortBy=PolicyCount")).Should().Equal(AuthRoleNames.TenantAdmin, "Alpha", "Beta");
    }

    [Fact]
    public async Task Roles_FilterByTypeAndIsSystem()
    {
        var s = await SeedAsync();

        (await RoleNamesAsync(s.Org, "filters[roleType]=Workspace")).Should().Equal("Beta");
        (await RoleNamesAsync(s.Org, "filters[roleType]=1")).Should().Equal("Alpha");
        (await RoleNamesAsync(s.Org, "filters[isSystem]=true")).Should().Equal(AuthRoleNames.TenantAdmin);
        (await RoleNamesAsync(s.Org, "filters[isSystem]=false")).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task Roles_SearchNameAndDescription()
    {
        var s = await SeedAsync();

        (await RoleNamesAsync(s.Org, "searchTerm=ALP")).Should().Equal("Alpha");
        (await RoleNamesAsync(s.Org, "searchTerm=zeta")).Should().Equal("Beta");
    }

    [Fact]
    public async Task Roles_UnknownFieldsIgnoredAndPagingStable()
    {
        var s = await SeedAsync();

        (await RoleNamesAsync(s.Org, "sortBy=TenantId%20desc&filters[Name]=Beta&containsFilters[Description]=zzz"))
            .Should().Equal("Alpha", "Beta", AuthRoleNames.TenantAdmin);

        var pages = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            pages.AddRange(await RoleNamesAsync(s.Org, $"sortBy=roleType&pageSize=1&pageNumber={page}"));
        }

        pages.Should().Equal(AuthRoleNames.TenantAdmin, "Alpha", "Beta");
    }

    [Fact]
    public async Task Roles_OtherTenant_NotListed()
    {
        var s = await SeedAsync();
        var other = await SeedOrganizationAsync();
        await SeedRoleAsync(other.TenantId, "Alpha");

        (await RoleNamesAsync(s.Org, "searchTerm=Alpha")).Should().Equal("Alpha");
        (await ItemsAsync(other, "/api/roles")).Select(i => i.GetProperty("policyCount").GetInt32()).Should().OnlyContain(c => c == 0);
    }

    [Fact]
    public async Task Role_GetAndDefault_ReportThePolicyCount()
    {
        var s = await SeedAsync();

        var beta = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/roles/{s.BetaId}", s.Org.AdminToken));
        var defaultRole = await DataAsync(await SendAsync(HttpMethod.Get, "/api/roles/default", s.Org.AdminToken));

        beta.GetProperty("policyCount").GetInt32().Should().Be(2);
        defaultRole.GetProperty("id").GetGuid().Should().Be(s.AlphaId);
        defaultRole.GetProperty("policyCount").GetInt32().Should().Be(1);
    }

    // ── Policies ────────────────────────────────────────────

    [Fact]
    public async Task Policies_NoSort_ByName()
    {
        var s = await SeedAsync();

        (await PolicyNamesAsync(s, "")).Should().Equal("Reader", "Writer");
    }

    [Fact]
    public async Task Policies_SortByNameRoleCountAndPermissionCount()
    {
        var s = await SeedAsync();

        (await PolicyNamesAsync(s, "sortBy=name%20desc")).Should().Equal("Writer", "Reader");
        (await PolicyNamesAsync(s, "sortBy=roleCount%20desc")).Should().Equal("Reader", "Writer");
        (await PolicyNamesAsync(s, "sortBy=roleCount")).Should().Equal("Writer", "Reader");
        (await PolicyNamesAsync(s, "sortBy=permissionCount%20desc")).Should().Equal("Reader", "Writer");
    }

    [Fact]
    public async Task Policies_SearchNameAndDescription_UnknownFieldsIgnored()
    {
        var s = await SeedAsync();

        (await PolicyNamesAsync(s, "searchTerm=WRIT")).Should().Equal("Writer");
        (await PolicyNamesAsync(s, "searchTerm=reads%20things")).Should().Equal("Reader");
        (await PolicyNamesAsync(s, "sortBy=TenantId&filters[Name]=Writer")).Should().Equal("Reader", "Writer");
    }

    [Fact]
    public async Task Policies_CountsAndKeys_AsBefore()
    {
        var s = await SeedAsync();

        var reader = (await ItemsAsync(s.Org, "/api/policies")).First();

        reader.GetProperty("roleCount").GetInt32().Should().Be(2);
        reader.GetProperty("permissionKeys").EnumerateArray().Select(k => k.GetString())
            .Should().Equal(PermissionKeysAuth.MembersRead, PermissionKeysAuth.RolesRead);
    }

    [Fact]
    public async Task Policies_OtherTenant_NotListed()
    {
        var s = await SeedAsync();
        var other = await SeedOrganizationAsync();

        (await ItemsAsync(other, "/api/policies")).Should().BeEmpty();
        (await PolicyNamesAsync(s, "")).Should().HaveCount(2);
    }
}
