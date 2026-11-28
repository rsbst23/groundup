using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-97d: the roles' member counts (counted on read) and <c>GET /api/roles/{id}/members</c>, against Postgres.
/// </summary>
[Collection("AuthFlow")]
public sealed class RoleMembersApiTests : AdminApiTestBase
{
    public RoleMembersApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Alpha is held by the member (twice: Manual and Invitation), by Dave (Pending) and by Erin
    /// (Inactive, never counted). The admin holds TenantAdmin.
    /// </summary>
    private sealed record Seeded(Organization Org, Guid AlphaId, Guid DaveId, Guid ErinId);

    private async Task<Seeded> SeedAsync()
    {
        var org = await SeedOrganizationAsync();
        var s = org.Suffix;
        var alpha = await SeedRoleAsync(org.TenantId, "Alpha");
        var dave = await SeedUserAsync($"kc-dave-{s}", $"dave-{s}@example.com", "Dave");
        await SeedMembershipAsync(dave, org.TenantId);
        var erin = await SeedUserAsync($"kc-erin-{s}", $"erin-{s}@example.com", "Erin");
        await SeedMembershipAsync(erin, org.TenantId, isActive: false);

        await using var context = Fixture.CreateAuthDbContext();
        await context.Set<UserTenant>()
            .Where(ut => ut.UserId == dave && ut.TenantId == org.TenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(ut => ut.Status, MembershipStatus.Pending));
        context.Set<UserRole>().AddRange(
            new UserRole { UserId = org.MemberId, RoleId = alpha, TenantId = org.TenantId, Source = RoleAssignmentSource.Manual },
            new UserRole { UserId = org.MemberId, RoleId = alpha, TenantId = org.TenantId, Source = RoleAssignmentSource.Invitation },
            new UserRole { UserId = dave, RoleId = alpha, TenantId = org.TenantId, Source = RoleAssignmentSource.Invitation },
            new UserRole { UserId = erin, RoleId = alpha, TenantId = org.TenantId, Source = RoleAssignmentSource.Manual });
        await context.SaveChangesAsync();
        return new Seeded(org, alpha, dave, erin);
    }

    private static string Email(Organization org, string name) => $"{name}-{org.Suffix}@example.com";

    private async Task<List<JsonElement>> ItemsAsync(Organization org, string path, string? token = null)
    {
        var response = await SendAsync(HttpMethod.Get, path, token ?? org.AdminToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    // ── Counts ──────────────────────────────────────────────

    [Fact]
    public async Task RolesList_CountsActiveAndPendingPeopleOnce()
    {
        var s = await SeedAsync();

        var items = await ItemsAsync(s.Org, "/api/roles");

        var alpha = items.Single(i => i.GetProperty("name").GetString() == "Alpha");
        alpha.GetProperty("activeMemberCount").GetInt32().Should().Be(1);   // the member, despite two sources
        alpha.GetProperty("pendingMemberCount").GetInt32().Should().Be(1);  // Dave; Erin is Inactive
        var admin = items.Single(i => i.GetProperty("name").GetString() == AuthRoleNames.TenantAdmin);
        admin.GetProperty("activeMemberCount").GetInt32().Should().Be(1);
        admin.GetProperty("pendingMemberCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RolesList_SortsByEachCount()
    {
        var s = await SeedAsync();
        await SeedRoleAsync(s.Org.TenantId, "Empty");

        var byPending = (await ItemsAsync(s.Org, "/api/roles?sortBy=pendingMemberCount%20desc,name"))
            .Select(i => i.GetProperty("name").GetString()).ToList();
        var byActive = (await ItemsAsync(s.Org, "/api/roles?sortBy=activeMemberCount,name"))
            .Select(i => i.GetProperty("name").GetString()).ToList();

        byPending.First().Should().Be("Alpha");
        byActive.First().Should().Be("Empty");
    }

    [Fact]
    public async Task RoleGet_ReportsTheCounts()
    {
        var s = await SeedAsync();

        var alpha = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/roles/{s.AlphaId}", s.Org.AdminToken));

        alpha.GetProperty("activeMemberCount").GetInt32().Should().Be(1);
        alpha.GetProperty("pendingMemberCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Counts_OtherTenantsMembersNotCounted()
    {
        var s = await SeedAsync();
        var other = await SeedOrganizationAsync();

        var items = await ItemsAsync(other, "/api/roles");

        items.Single().GetProperty("activeMemberCount").GetInt32().Should().Be(1); // only the other org's admin
    }

    // ── Role members ────────────────────────────────────────

    [Fact]
    public async Task RoleMembers_ListsActiveAndPendingHolders()
    {
        var s = await SeedAsync();

        var items = await ItemsAsync(s.Org, $"/api/roles/{s.AlphaId}/members");

        items.Select(i => i.GetProperty("email").GetString()).Should().Equal(Email(s.Org, "amember"), Email(s.Org, "dave"));
        items[0].GetProperty("roles").EnumerateArray().Single().GetProperty("sources").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task RoleMembers_FilterSearchAndSort()
    {
        var s = await SeedAsync();

        (await ItemsAsync(s.Org, $"/api/roles/{s.AlphaId}/members?filters[status]=Pending"))
            .Select(i => i.GetProperty("email").GetString()).Should().Equal(Email(s.Org, "dave"));
        (await ItemsAsync(s.Org, $"/api/roles/{s.AlphaId}/members?searchTerm=DAV"))
            .Select(i => i.GetProperty("email").GetString()).Should().Equal(Email(s.Org, "dave"));
        (await ItemsAsync(s.Org, $"/api/roles/{s.AlphaId}/members?sortBy=email%20desc"))
            .Select(i => i.GetProperty("email").GetString()).Should().Equal(Email(s.Org, "dave"), Email(s.Org, "amember"));
    }

    [Fact]
    public async Task RoleMembers_RoleOfAnotherTenant_NotFound()
    {
        var s = await SeedAsync();
        var other = await SeedOrganizationAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/roles/{s.AlphaId}/members", other.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RoleMembers_WithRolesReadOnly_NeedsMembersRead()
    {
        var s = await SeedAsync();
        await GrantPermissionsAsync(s.Org, s.Org.MemberId, PermissionKeysAuth.RolesRead);

        var response = await SendAsync(HttpMethod.Get, $"/api/roles/{s.AlphaId}/members", s.Org.MemberToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("PERMISSION_REQUIRED");
        (await ErrorsAsync(response)).Should().Equal(PermissionKeysAuth.MembersRead);
    }
}
