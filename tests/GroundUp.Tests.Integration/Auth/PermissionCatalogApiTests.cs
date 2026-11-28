using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-97d: the paged permission catalogue (<c>GET /api/permissions</c>) and its modules, against Postgres.
/// Each test uses its own module name, so the shared catalogue of other tests does not interfere.
/// </summary>
[Collection("AuthFlow")]
public sealed class PermissionCatalogApiTests : AdminApiTestBase
{
    public PermissionCatalogApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>A module with three permissions: zeta (with a description), alpha and mid.</summary>
    private async Task<(Organization Org, string Module)> SeedAsync()
    {
        var org = await SeedOrganizationAsync();
        var module = $"cat{org.Suffix}";
        await using var context = Fixture.CreateAuthDbContext();
        context.Permissions.AddRange(
            new Permission { Key = $"{module}.zeta", Name = "Zeta thing", Module = module, Description = "Needle in the description" },
            new Permission { Key = $"{module}.alpha", Name = "Alpha thing", Module = module },
            new Permission { Key = $"{module}.mid", Name = "Mid thing", Module = module });
        await context.SaveChangesAsync();
        return (org, module);
    }

    private async Task<List<JsonElement>> ItemsAsync(Organization org, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/permissions?{query}", org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private static List<string> Keys(IEnumerable<JsonElement> items) => items.Select(i => i.GetProperty("key").GetString()!).ToList();

    [Fact]
    public async Task ModuleFilter_ByKeyUnlessSorted()
    {
        var (org, module) = await SeedAsync();

        Keys(await ItemsAsync(org, $"filters[module]={module}")).Should().Equal($"{module}.alpha", $"{module}.mid", $"{module}.zeta");
    }

    [Fact]
    public async Task SortByNameAndKey()
    {
        var (org, module) = await SeedAsync();

        Keys(await ItemsAsync(org, $"filters[module]={module}&sortBy=name%20desc"))
            .Should().Equal($"{module}.zeta", $"{module}.mid", $"{module}.alpha");
        Keys(await ItemsAsync(org, $"filters[module]={module}&sortBy=key%20desc"))
            .Should().Equal($"{module}.zeta", $"{module}.mid", $"{module}.alpha");
    }

    [Fact]
    public async Task Search_NameKeyAndDescription()
    {
        var (org, module) = await SeedAsync();

        Keys(await ItemsAsync(org, $"filters[module]={module}&searchTerm=needle%20in%20the")).Should().Equal($"{module}.zeta");
        Keys(await ItemsAsync(org, $"searchTerm={module}.MID")).Should().Equal($"{module}.mid");
        Keys(await ItemsAsync(org, $"filters[module]={module}&searchTerm=ALPHA%20THING")).Should().Equal($"{module}.alpha");
    }

    [Fact]
    public async Task Paging_WithHeaders()
    {
        var (org, module) = await SeedAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/permissions?filters[module]={module}&pageSize=2&pageNumber=2", org.AdminToken);

        response.Headers.GetValues("X-Total-Count").Single().Should().Be("3");
        Keys((await DataAsync(response)).GetProperty("items").EnumerateArray()).Should().Equal($"{module}.zeta");
    }

    [Fact]
    public async Task SystemOnlyKeys_NeverListed()
    {
        var (org, _) = await SeedAsync();
        foreach (var key in PermissionKeysAuth.SystemOnly)
        {
            await EnsurePermissionAsync(key);
        }

        var keys = Keys(await ItemsAsync(org, "searchTerm=auth.&pageSize=100"));

        keys.Should().NotContain(PermissionKeysAuth.SystemOnly);
    }

    [Fact]
    public async Task Modules_SortedWithoutSystemOnlyOnlyModules()
    {
        var (org, module) = await SeedAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/permissions/modules", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await DataAsync(response)).EnumerateArray().Select(m => m.GetString()).ToList();
        modules.Should().Contain(module).And.OnlyHaveUniqueItems();
        modules.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task PlainMember_Forbidden()
    {
        var (org, _) = await SeedAsync();

        (await SendAsync(HttpMethod.Get, "/api/permissions/modules", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
