using System.Net;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-96: the permission catalogue (<c>/api/permissions</c>) and policy endpoints
/// (<c>/api/policies</c>) through the sample app, with real permissions, the unique name index, the
/// foreign keys and the escalation guard.
/// </summary>
[Collection("AuthFlow")]
public sealed class PoliciesApiTests : AdminApiTestBase
{
    public PoliciesApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── Catalogue ─────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_GroupedByModule_WithoutSystemOnlyKeys()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        foreach (var key in PermissionKeysAuth.SystemOnly)
        {
            await EnsurePermissionAsync(key);
        }

        var response = await SendAsync(HttpMethod.Get, "/api/permissions", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var modules = (await DataAsync(response)).EnumerateArray().ToList();
        modules.Select(m => m.GetProperty("module").GetString()).Should().BeInAscendingOrder(StringComparer.Ordinal);
        var keys = modules.SelectMany(m => m.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("key").GetString())).ToList();
        keys.Should().Contain(PermissionKeysAuth.MembersRead);
        keys.Should().NotContain(PermissionKeysAuth.SystemOnly);
    }

    [Fact]
    public async Task PlainMember_Forbidden()
    {
        var org = await SeedOrganizationAsync();

        (await SendAsync(HttpMethod.Get, "/api/permissions", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/policies", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/policies", org.MemberToken, new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CRUD ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGetRenameSetPermissionsDelete()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        await EnsurePermissionAsync(PermissionKeysAuth.InvitationsSend);

        var created = await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken,
            new { name = " Member Admins ", description = "Manage members", permissionKeys = new[] { PermissionKeysAuth.MembersRead } });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = await DataAsync(created);
        data.GetProperty("name").GetString().Should().Be("Member Admins");
        data.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("key").GetString()).Should().Equal(PermissionKeysAuth.MembersRead);
        var id = data.GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Get, $"/api/policies/{id}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var renamed = await SendAsync(HttpMethod.Put, $"/api/policies/{id}", org.AdminToken, new { name = "Membership" });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(renamed)).GetProperty("name").GetString().Should().Be("Membership");

        var set = await SendAsync(HttpMethod.Put, $"/api/policies/{id}/permissions", org.AdminToken,
            new { permissionKeys = new[] { PermissionKeysAuth.InvitationsSend } });
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(set)).GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("key").GetString())
            .Should().Equal(PermissionKeysAuth.InvitationsSend);

        await using (var context = Fixture.CreateAuthDbContext())
        {
            var row = await context.Policies.AsNoTracking().SingleAsync(p => p.Id == id);
            row.Name.Should().Be("Membership");
            row.CreatedAt.Should().BeAfter(DateTime.UtcNow.AddHours(-1), "a rename keeps the creation time");
            (await context.PolicyPermissions.CountAsync(pp => pp.PolicyId == id)).Should().Be(1);
        }

        (await SendAsync(HttpMethod.Delete, $"/api/policies/{id}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var context = Fixture.CreateAuthDbContext())
        {
            (await context.Policies.AnyAsync(p => p.Id == id)).Should().BeFalse();
            (await context.PolicyPermissions.AnyAsync(pp => pp.PolicyId == id)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task List_PagedWithKeysRoleCountsAndHeaders()
    {
        var org = await SeedOrganizationAsync();
        var (roleId, seededPolicy) = await GrantPermissionsAsync(org, org.MemberId, PermissionKeysAuth.MembersRead);
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            (await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name })).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var response = await SendAsync(HttpMethod.Get, "/api/policies?pageSize=2", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Total-Count").Single().Should().Be("3");
        var items = (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).Should().Equal("Alpha", "Beta");

        var all = await DataAsync(await SendAsync(HttpMethod.Get, "/api/policies?pageSize=10", org.AdminToken));
        var seeded = all.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == seededPolicy);
        seeded.GetProperty("roleCount").GetInt32().Should().Be(1);
        seeded.GetProperty("permissionKeys").EnumerateArray().Select(k => k.GetString()).Should().Equal(PermissionKeysAuth.MembersRead);
        roleId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_NameTakenInAnotherCase_PolicyNameTaken()
    {
        var org = await SeedOrganizationAsync();
        (await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name = "Editors" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name = "EDITORS" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.PolicyNameTaken);
    }

    [Fact]
    public async Task Create_TenConcurrentRequestsForOneName_ExactlyOneCreated()
    {
        var org = await SeedOrganizationAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name = i % 2 == 0 ? "Racer" : "racer" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
    }

    [Fact]
    public async Task Create_UnknownAndSystemOnlyKeys_BadRequest()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.RolesAssignSystem);

        var response = await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken,
            new { name = "Bad", permissionKeys = new[] { "nope.nope.nope", PermissionKeysAuth.RolesAssignSystem } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await ErrorsAsync(response);
        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains("nope.nope.nope"));
        errors.Should().Contain(e => e.Contains(PermissionKeysAuth.RolesAssignSystem));
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Policies.AnyAsync(p => p.TenantId == org.TenantId && p.Name == "Bad")).Should().BeFalse();
    }

    // ── Isolation and usage ───────────────────────────────────────

    [Fact]
    public async Task PolicyOfAnotherTenant_NotFoundForEveryOperation_AndNotListed()
    {
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        var foreign = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/policies", other.AdminToken, new { name = "Foreign" })))
            .GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Get, $"/api/policies/{foreign}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/policies/{foreign}", org.AdminToken, new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/policies/{foreign}/permissions", org.AdminToken, new { permissionKeys = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/policies/{foreign}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await DataAsync(await SendAsync(HttpMethod.Get, "/api/policies?pageSize=100", org.AdminToken));
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(foreign);
    }

    [Fact]
    public async Task Delete_UsedByARole_PolicyInUseNamingIt()
    {
        var org = await SeedOrganizationAsync();
        var policyId = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name = "Used" }))).GetProperty("id").GetGuid();
        var roleId = await SeedRoleAsync(org.TenantId, "Editor");
        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Delete, $"/api/policies/{policyId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.PolicyInUse);
        (await ErrorsAsync(response)).Should().Equal("Used by role 'Editor'.");
    }

    [Fact]
    public async Task Delete_LinkTheUsageCheckDoesNotSee_TheForeignKeyRefusesIt()
    {
        // A role attached between the usage check and the delete: simulated with a link to another
        // tenant's role, which the usage check does not list but the foreign key still protects
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        var policyId = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name = "Raced" }))).GetProperty("id").GetGuid();
        var foreignRole = await SeedRoleAsync(other.TenantId, "Intruder");
        await using (var context = Fixture.CreateAuthDbContext())
        {
            context.Set<RolePolicy>().Add(new RolePolicy { RoleId = foreignRole, PolicyId = policyId });
            await context.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Delete, $"/api/policies/{policyId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.PolicyInUse);
        await using var verify = Fixture.CreateAuthDbContext();
        (await verify.Policies.AnyAsync(p => p.Id == policyId)).Should().BeTrue();
    }

    // ── Escalation guard ──────────────────────────────────────────

    [Fact]
    public async Task SetPermissions_MemberAddsAKeyTheyLack_PermissionNotHeld_ButAKeyTheyHoldIsFine()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        await GrantPermissionsAsync(org, org.MemberId, PermissionKeysAuth.PermissionsRead, PermissionKeysAuth.PermissionsWrite);
        var policyId = (await DataAsync(await SendAsync(HttpMethod.Post, "/api/policies", org.MemberToken, new { name = "Member made" })))
            .GetProperty("id").GetGuid();

        var refused = await SendAsync(HttpMethod.Put, $"/api/policies/{policyId}/permissions", org.MemberToken,
            new { permissionKeys = new[] { PermissionKeysAuth.PermissionsRead, PermissionKeysAuth.MembersRead } });
        var allowed = await SendAsync(HttpMethod.Put, $"/api/policies/{policyId}/permissions", org.MemberToken,
            new { permissionKeys = new[] { PermissionKeysAuth.PermissionsRead } });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(refused)).Should().Be(AuthErrorCodes.PermissionNotHeld);
        (await ErrorsAsync(refused)).Should().Equal(PermissionKeysAuth.MembersRead);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_MemberWithKeysTheyLack_PermissionNotHeld()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersManage);
        await GrantPermissionsAsync(org, org.MemberId, PermissionKeysAuth.PermissionsWrite);

        var response = await SendAsync(HttpMethod.Post, "/api/policies", org.MemberToken,
            new { name = "Grab", permissionKeys = new[] { PermissionKeysAuth.MembersManage } });

        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.PermissionNotHeld);
    }
}
