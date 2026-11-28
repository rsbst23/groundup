using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-96: role policies (<c>/api/roles/{id}/policies</c>), a role's permissions, the escalation guard
/// on attaching policies and granting roles, the permission cache (members see changes without
/// signing in again), and the permissions in <c>/auth/me</c>, through the sample app.
/// </summary>
[Collection("AuthFlow")]
public sealed class RolePoliciesApiTests : AdminApiTestBase
{
    public RolePoliciesApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── Role policies ─────────────────────────────────────────────

    [Fact]
    public async Task AttachListPermissionsDetach()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        await EnsurePermissionAsync(PermissionKeysAuth.InvitationsSend);
        var readers = await CreatePolicyAsync(org, "Readers", PermissionKeysAuth.MembersRead);
        var inviters = await CreatePolicyAsync(org, "Inviters", PermissionKeysAuth.MembersRead, PermissionKeysAuth.InvitationsSend);
        var roleId = await SeedRoleAsync(org.TenantId, "Coordinator");

        var first = await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{readers}", org.AdminToken);
        var again = await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{readers}", org.AdminToken);
        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{inviters}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(again)).GetProperty("id").GetGuid().Should().Be((await DataAsync(first)).GetProperty("id").GetGuid());

        var policies = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/roles/{roleId}/policies", org.AdminToken));
        policies.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Equal("Inviters", "Readers");

        var permissions = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/roles/{roleId}/permissions", org.AdminToken));
        permissions.GetProperty("allPermissions").GetBoolean().Should().BeFalse();
        var byKey = permissions.GetProperty("permissions").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!);
        byKey.Keys.Should().BeEquivalentTo(PermissionKeysAuth.MembersRead, PermissionKeysAuth.InvitationsSend);
        GrantedBy(byKey[PermissionKeysAuth.MembersRead]).Should().Equal("Inviters", "Readers");
        GrantedBy(byKey[PermissionKeysAuth.InvitationsSend]).Should().Equal("Inviters");

        (await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}/policies/{readers}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}/policies/{readers}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantAdmin_PoliciesCannotChange_AndItHasAllPermissions()
    {
        var org = await SeedOrganizationAsync();
        var policyId = await CreatePolicyAsync(org, "Any");

        var attach = await SendAsync(HttpMethod.Post, $"/api/roles/{org.AdminRoleId}/policies/{policyId}", org.AdminToken);
        var permissions = await DataAsync(await SendAsync(HttpMethod.Get, $"/api/roles/{org.AdminRoleId}/permissions", org.AdminToken));

        attach.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(attach)).Should().Be(AuthErrorCodes.RoleIsBuiltIn);
        permissions.GetProperty("allPermissions").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Attach_PolicyOfAnotherTenant_NotFound()
    {
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        var foreignPolicy = await CreatePolicyAsync(other, "Foreign");
        var roleId = await SeedRoleAsync(org.TenantId, "Editor");

        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{foreignPolicy}", org.AdminToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Escalation guard ──────────────────────────────────────────

    [Fact]
    public async Task Attach_MemberWithRolesWriteAttachesAPolicyGrantingMore_PermissionNotHeld()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersManage);
        await GrantPermissionsAsync(org, org.MemberId, PermissionKeysAuth.RolesWrite);
        var policyId = await CreatePolicyAsync(org, "Removers", PermissionKeysAuth.MembersManage);
        var roleId = await SeedRoleAsync(org.TenantId, "Remover");

        var response = await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{policyId}", org.MemberToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.PermissionNotHeld);
        (await ErrorsAsync(response)).Should().Equal(PermissionKeysAuth.MembersManage);
    }

    [Fact]
    public async Task GrantRole_AssignHolderGrantsARoleWithPermissionsTheyLack_PermissionNotHeld()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersManage);
        var grantorId = await SeedExtraMemberAsync(org);
        await GrantPermissionsAsync(org, grantorId, PermissionKeysAuth.RolesAssign);
        var grantorToken = Fixture.GenerateTestToken(grantorId, org.TenantId);
        var roleId = await SeedRoleAsync(org.TenantId, "Remover");
        var policyId = await CreatePolicyAsync(org, "Removers", PermissionKeysAuth.MembersManage);
        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var refused = await SendAsync(HttpMethod.Post, $"/api/members/{org.MemberId}/roles/{roleId}", grantorToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(refused)).Should().Be(AuthErrorCodes.PermissionNotHeld);

        // Once the role grants nothing the grantor lacks, the same grant works
        (await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"/api/members/{org.MemberId}/roles/{roleId}", grantorToken)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Cache ─────────────────────────────────────────────────────

    [Fact]
    public async Task Member_GainsAndLosesAPermissionWithoutSigningInAgain()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        var roleId = await SeedRoleAsync(org.TenantId, "Viewer");
        await AssignRoleToUserAsync(org.MemberId, roleId, org.TenantId);
        var policyId = await CreatePolicyAsync(org, "Viewers");
        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        // The member's permissions are now cached (empty)
        (await SendAsync(HttpMethod.Get, "/api/members", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SendAsync(HttpMethod.Put, $"/api/policies/{policyId}/permissions", org.AdminToken,
            new { permissionKeys = new[] { PermissionKeysAuth.MembersRead } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/members", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.OK, "the same token sees the new permission");

        (await SendAsync(HttpMethod.Put, $"/api/policies/{policyId}/permissions", org.AdminToken,
            new { permissionKeys = Array.Empty<string>() })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/members", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "and loses it");
    }

    [Fact]
    public async Task Member_LosesAPermissionWhenThePolicyIsDetached()
    {
        var org = await SeedOrganizationAsync();
        await EnsurePermissionAsync(PermissionKeysAuth.MembersRead);
        var roleId = await SeedRoleAsync(org.TenantId, "Viewer");
        await AssignRoleToUserAsync(org.MemberId, roleId, org.TenantId);
        var policyId = await CreatePolicyAsync(org, "Viewers", PermissionKeysAuth.MembersRead);
        (await SendAsync(HttpMethod.Post, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Get, "/api/members", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}/policies/{policyId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Get, "/api/members", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── /auth/me ──────────────────────────────────────────────────

    [Fact]
    public async Task Me_Member_ListsTheirPermissions()
    {
        var org = await SeedOrganizationAsync();
        await GrantPermissionsAsync(org, org.MemberId, PermissionKeysAuth.MembersRead, PermissionKeysAuth.InvitationsSend);

        var me = await BodyAsync(await SendAsync(HttpMethod.Get, "/auth/me", org.MemberToken));

        me.GetProperty("allPermissions").GetBoolean().Should().BeFalse();
        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal(new[] { PermissionKeysAuth.InvitationsSend, PermissionKeysAuth.MembersRead }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Me_TenantAdmin_AllPermissions()
    {
        var org = await SeedOrganizationAsync();

        var me = await BodyAsync(await SendAsync(HttpMethod.Get, "/auth/me", org.AdminToken));

        me.GetProperty("allPermissions").GetBoolean().Should().BeTrue();
        me.GetProperty("permissions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Me_Tenantless_Nothing()
    {
        var org = await SeedOrganizationAsync();

        var me = await BodyAsync(await SendAsync(HttpMethod.Get, "/auth/me", Fixture.GenerateTestToken(org.AdminId)));

        me.GetProperty("allPermissions").GetBoolean().Should().BeFalse("TenantAdmin counts only in its tenant");
        me.GetProperty("permissions").GetArrayLength().Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private async Task<Guid> CreatePolicyAsync(Organization org, string name, params string[] keys)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/policies", org.AdminToken, new { name, permissionKeys = keys });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedExtraMemberAsync(Organization org)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userId = await SeedUserAsync($"kc-extra-{suffix}", $"extra-{suffix}@example.com");
        await SeedMembershipAsync(userId, org.TenantId);
        return userId;
    }

    private static List<string?> GrantedBy(JsonElement permission)
        => permission.GetProperty("grantedBy").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
}
