using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-95: the roles endpoints (<c>/api/roles</c>) through the sample app, with real permissions, the
/// unique name index and the foreign keys.
/// </summary>
[Collection("AuthFlow")]
public sealed class RolesApiTests : AuthFlowTestBase
{
    public RolesApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ── List, get, create, rename ─────────────────────────────────

    [Fact]
    public async Task List_AsTenantAdmin_PagedWithHeadersAndTheDefaultMarked()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");
        await SeedRoleAsync(org.TenantId, "Viewer");
        await SetDefaultRoleAsync(org.TenantId, editorId);

        var response = await SendAsync(HttpMethod.Get, "/api/roles?pageSize=2", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Total-Count").Single().Should().Be("3");
        response.Headers.GetValues("X-Total-Pages").Single().Should().Be("2");
        var items = (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).Should().Equal("Editor", AuthRoleNames.TenantAdmin);
        items[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
        items[1].GetProperty("isDefault").GetBoolean().Should().BeFalse();
        items[1].GetProperty("isSystem").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_RoleOfTheTenant_ReturnsIt()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");

        var response = await SendAsync(HttpMethod.Get, $"/api/roles/{editorId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(response);
        data.GetProperty("name").GetString().Should().Be("Editor");
        data.GetProperty("isDefault").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithoutAType_CreatesAnApplicationRole_ThenRename()
    {
        var org = await SeedOrganizationAsync();

        var created = await SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = "  Reviewer ", description = "Reviews" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = await DataAsync(created);
        data.GetProperty("name").GetString().Should().Be("Reviewer");
        data.GetProperty("roleType").GetInt32().Should().Be((int)RoleType.Application);
        data.GetProperty("tenantId").GetGuid().Should().Be(org.TenantId);
        var id = data.GetProperty("id").GetGuid();
        DateTime createdAt;
        string? createdBy;
        await using (var before = Fixture.CreateAuthDbContext())
        {
            var created1 = await before.Roles.AsNoTracking().SingleAsync(r => r.Id == id);
            (createdAt, createdBy) = (created1.CreatedAt, created1.CreatedBy);
        }

        var renamed = await SendAsync(HttpMethod.Put, $"/api/roles/{id}", org.AdminToken, new { name = "Approver", description = (string?)null });

        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = Fixture.CreateAuthDbContext();
        var row = await context.Roles.AsNoTracking().SingleAsync(r => r.Id == id);
        row.Name.Should().Be("Approver");
        row.Description.Should().BeNull();
        row.RoleType.Should().Be(RoleType.Application);
        createdAt.Should().BeAfter(DateTime.UtcNow.AddHours(-1), "the insert sets it");
        row.CreatedAt.Should().Be(createdAt, "a rename keeps the creation time");
        row.CreatedBy.Should().Be(createdBy).And.NotBeNull();
        row.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WorkspaceType_Created()
    {
        var org = await SeedOrganizationAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = "Space", roleType = (int)RoleType.Workspace });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await DataAsync(response)).GetProperty("roleType").GetInt32().Should().Be((int)RoleType.Workspace);
    }

    [Fact]
    public async Task Create_SystemType_BadRequest()
    {
        var org = await SeedOrganizationAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = "Root", roleType = (int)RoleType.System });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(response)).Should().Be("VALIDATION_FAILED");
    }

    // ── Names ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_NameTakenInAnotherCase_RoleNameTaken()
    {
        var org = await SeedOrganizationAsync();
        await SeedRoleAsync(org.TenantId, "Editor");

        var response = await SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = "EDITOR" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.RoleNameTaken);
    }

    [Fact]
    public async Task Rename_ToATakenName_RoleNameTaken()
    {
        var org = await SeedOrganizationAsync();
        await SeedRoleAsync(org.TenantId, "Editor");
        var viewerId = await SeedRoleAsync(org.TenantId, "Viewer");

        var response = await SendAsync(HttpMethod.Put, $"/api/roles/{viewerId}", org.AdminToken, new { name = "editor" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.RoleNameTaken);
    }

    [Fact]
    public async Task Create_SameNameInAnotherTenant_Created()
    {
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        await SeedRoleAsync(other.TenantId, "Editor");

        (await SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = "Editor" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_TenConcurrentRequestsForOneName_ExactlyOneCreated()
    {
        var org = await SeedOrganizationAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            SendAsync(HttpMethod.Post, "/api/roles", org.AdminToken, new { name = i % 2 == 0 ? "Racer" : "racer" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Roles.CountAsync(r => r.TenantId == org.TenantId && r.Name.ToLower() == "racer")).Should().Be(1);
    }

    // ── Tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task RoleOfAnotherTenant_NotFoundForEveryOperation_AndNotListed()
    {
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        var foreignId = await SeedRoleAsync(other.TenantId, "Foreign");

        (await SendAsync(HttpMethod.Get, $"/api/roles/{foreignId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/roles/{foreignId}", org.AdminToken, new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/api/roles/{foreignId}", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/api/roles/{foreignId}/default", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await SendAsync(HttpMethod.Get, "/api/roles?pageSize=100", org.AdminToken);
        (await DataAsync(list)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(foreignId);

        await using var context = Fixture.CreateAuthDbContext();
        (await context.Roles.AsNoTracking().SingleAsync(r => r.Id == foreignId)).Name.Should().Be("Foreign");
        (await context.Tenants.AsNoTracking().SingleAsync(t => t.Id == org.TenantId)).DefaultRoleId.Should().BeNull();
    }

    // ── Built-in TenantAdmin ──────────────────────────────────────

    [Theory]
    [InlineData("Administrators")]
    [InlineData("tenantadmin")]
    public async Task TenantAdmin_Rename_RoleIsBuiltIn(string name)
    {
        var org = await SeedOrganizationAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/roles/{org.AdminRoleId}", org.AdminToken, new { name });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.RoleIsBuiltIn);
    }

    [Fact]
    public async Task TenantAdmin_DescriptionChange_Saved()
    {
        var org = await SeedOrganizationAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/roles/{org.AdminRoleId}", org.AdminToken,
            new { name = AuthRoleNames.TenantAdmin, description = "Runs the organization" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = Fixture.CreateAuthDbContext();
        var row = await context.Roles.AsNoTracking().SingleAsync(r => r.Id == org.AdminRoleId);
        row.Description.Should().Be("Runs the organization");
        row.IsSystem.Should().BeTrue();
    }

    [Fact]
    public async Task TenantAdmin_DeleteOrMakeDefault_RoleIsBuiltIn()
    {
        var org = await SeedOrganizationAsync();

        var delete = await SendAsync(HttpMethod.Delete, $"/api/roles/{org.AdminRoleId}", org.AdminToken);
        var makeDefault = await SendAsync(HttpMethod.Put, $"/api/roles/{org.AdminRoleId}/default", org.AdminToken);

        (await ErrorCodeAsync(delete)).Should().Be(AuthErrorCodes.RoleIsBuiltIn);
        (await ErrorCodeAsync(makeDefault)).Should().Be(AuthErrorCodes.RoleIsBuiltIn);
    }

    // ── Delete ────────────────────────────────────────────────────

    public enum Usage { Holder, PendingInvitation, ExpiredInvitation, ActiveJoinLink, TenantDefault }

    [Theory]
    [InlineData(Usage.Holder, "Held by 1 member.")]
    [InlineData(Usage.PendingInvitation, "Used by 1 pending or expired invitation.")]
    [InlineData(Usage.ExpiredInvitation, "Used by 1 pending or expired invitation.")]
    [InlineData(Usage.ActiveJoinLink, "Used by 1 active join link.")]
    [InlineData(Usage.TenantDefault, "It is the organization's default role for new members.")]
    public async Task Delete_RoleInUse_RoleInUseWithTheReason(Usage usage, string reason)
    {
        var org = await SeedOrganizationAsync();
        var roleId = await SeedRoleAsync(org.TenantId, "Editor");
        switch (usage)
        {
            case Usage.Holder:
                await AssignRoleToUserAsync(org.MemberId, roleId, org.TenantId);
                break;
            case Usage.PendingInvitation:
                await SeedInvitationAsync(org, roleId, InvitationStatus.Pending);
                break;
            case Usage.ExpiredInvitation:
                await SeedInvitationAsync(org, roleId, InvitationStatus.Expired);
                break;
            case Usage.ActiveJoinLink:
                await SeedJoinLinkAsync(org, roleId);
                break;
            case Usage.TenantDefault:
                await SetDefaultRoleAsync(org.TenantId, roleId);
                break;
        }

        var response = await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body.GetProperty("errorCode").GetString().Should().Be(AuthErrorCodes.RoleInUse);
        body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).Should().Equal(reason);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Roles.AnyAsync(r => r.Id == roleId)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_UnusedRole_DeletedWithItsPolicyLinks()
    {
        var org = await SeedOrganizationAsync();
        var roleId = await SeedRoleAsync(org.TenantId, "Editor");
        var policyId = await SeedPolicyAsync(org.TenantId, roleId);

        var response = await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Roles.AnyAsync(r => r.Id == roleId)).Should().BeFalse();
        (await context.Set<RolePolicy>().AnyAsync(rp => rp.RoleId == roleId)).Should().BeFalse();
        (await context.Policies.AnyAsync(p => p.Id == policyId)).Should().BeTrue("only the link goes, not the policy");
    }

    [Fact]
    public async Task Delete_GrantTheUsageCheckDoesNotSee_TheForeignKeyRefusesIt()
    {
        // A grant committed between the usage check and the delete: simulated with a row the check
        // does not count (recorded under another tenant), which the foreign key still protects
        var org = await SeedOrganizationAsync();
        var other = await SeedOrganizationAsync();
        var roleId = await SeedRoleAsync(org.TenantId, "Editor");
        await AssignRoleToUserAsync(other.MemberId, roleId, other.TenantId);

        var response = await SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}", org.AdminToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.RoleInUse);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Roles.AnyAsync(r => r.Id == roleId)).Should().BeTrue();
        (await context.UserRoles.AnyAsync(ur => ur.RoleId == roleId)).Should().BeTrue("no grant is deleted with a role");
    }

    // ── Default role ──────────────────────────────────────────────

    [Fact]
    public async Task Default_SetGetAndClear()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");

        var none = await SendAsync(HttpMethod.Get, "/api/roles/default", org.AdminToken);
        none.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(none)).ValueKind.Should().Be(JsonValueKind.Null);

        var set = await SendAsync(HttpMethod.Put, $"/api/roles/{editorId}/default", org.AdminToken);
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(set)).GetProperty("isDefault").GetBoolean().Should().BeTrue();

        var get = await SendAsync(HttpMethod.Get, "/api/roles/default", org.AdminToken);
        (await DataAsync(get)).GetProperty("id").GetGuid().Should().Be(editorId);

        (await SendAsync(HttpMethod.Delete, "/api/roles/default", org.AdminToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Tenants.AsNoTracking().SingleAsync(t => t.Id == org.TenantId)).DefaultRoleId.Should().BeNull();
    }

    [Fact]
    public async Task SetDefault_CallerWithWriteButNotAssign_Forbidden()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");
        await GrantPermissionsAsync(org, PermissionKeysAuth.RolesRead, PermissionKeysAuth.RolesWrite);

        var response = await SendAsync(HttpMethod.Put, $"/api/roles/{editorId}/default", org.MemberToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = Fixture.CreateAuthDbContext();
        (await context.Tenants.AsNoTracking().SingleAsync(t => t.Id == org.TenantId)).DefaultRoleId.Should().BeNull();
    }

    [Fact]
    public async Task SetDefault_CallerWithWriteAndAssign_Saved()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");
        await GrantPermissionsAsync(org, PermissionKeysAuth.RolesWrite, PermissionKeysAuth.RolesAssign);

        (await SendAsync(HttpMethod.Put, $"/api/roles/{editorId}/default", org.MemberToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Permissions ───────────────────────────────────────────────

    [Fact]
    public async Task PlainMember_ForbiddenEverywhere()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");

        (await SendAsync(HttpMethod.Get, "/api/roles", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/roles/default", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/roles", org.MemberToken, new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/roles/{editorId}", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReadOnlyMember_ListsButCannotCreateOrDelete()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");
        await GrantPermissionsAsync(org, PermissionKeysAuth.RolesRead);

        (await SendAsync(HttpMethod.Get, "/api/roles", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, "/api/roles", org.MemberToken, new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Delete, $"/api/roles/{editorId}", org.MemberToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoTenantSelected_Forbidden()
    {
        var org = await SeedOrganizationAsync();
        var tenantless = Fixture.GenerateTestToken(org.AdminId);

        var response = await SendAsync(HttpMethod.Get, "/api/roles", tenantless);

        // The authorization proxy refuses first: without a tenant the caller has no tenant permissions.
        // The service's own TENANT_REQUIRED check applies to callers that pass the proxy (unit tests).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("FORBIDDEN");
    }

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Organization(
        Guid TenantId, Guid AdminId, Guid AdminRoleId, string AdminToken, Guid MemberId, string MemberToken, string Suffix);

    /// <summary>A tenant with a TenantAdmin and a plain member, each with a token for the tenant.</summary>
    private async Task<Organization> SeedOrganizationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Roles Org {suffix}", $"roles-org-{suffix}");
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);

        var adminId = await SeedUserAsync($"kc-radmin-{suffix}", $"radmin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);

        var memberId = await SeedUserAsync($"kc-rmember-{suffix}", $"rmember-{suffix}@example.com");
        await SeedMembershipAsync(memberId, tenantId);

        return new Organization(
            tenantId, adminId, adminRoleId, Fixture.GenerateTestToken(adminId, tenantId),
            memberId, Fixture.GenerateTestToken(memberId, tenantId), suffix);
    }

    /// <summary>Gives the member a role whose policy holds <paramref name="keys"/>.</summary>
    private async Task GrantPermissionsAsync(Organization org, params string[] keys)
    {
        var roleId = await SeedRoleAsync(org.TenantId, $"Custom {Guid.NewGuid():N}");
        await using (var context = Fixture.CreateAuthDbContext())
        {
            var policy = new Policy { Name = $"Policy {Guid.NewGuid():N}", TenantId = org.TenantId };
            context.Policies.Add(policy);
            foreach (var key in keys)
            {
                var permission = await context.Permissions.FirstOrDefaultAsync(p => p.Key == key);
                if (permission is null)
                {
                    permission = new Permission { Key = key, Name = key, Module = "auth" };
                    context.Permissions.Add(permission);
                }

                context.Set<PolicyPermission>().Add(new PolicyPermission { Policy = policy, Permission = permission });
            }

            context.Set<RolePolicy>().Add(new RolePolicy { RoleId = roleId, Policy = policy });
            await context.SaveChangesAsync();
        }

        await AssignRoleToUserAsync(org.MemberId, roleId, org.TenantId);
    }

    private async Task<Guid> SeedPolicyAsync(Guid tenantId, Guid roleId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var policy = new Policy { Name = $"Policy {Guid.NewGuid():N}", TenantId = tenantId };
        context.Policies.Add(policy);
        context.Set<RolePolicy>().Add(new RolePolicy { RoleId = roleId, Policy = policy });
        await context.SaveChangesAsync();
        return policy.Id;
    }

    private async Task SetDefaultRoleAsync(Guid tenantId, Guid roleId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        await context.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DefaultRoleId, roleId));
    }

    private async Task SeedInvitationAsync(Organization org, Guid roleId, InvitationStatus status)
    {
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        await using var context = Fixture.CreateAuthDbContext();
        context.Invitations.Add(new Invitation
        {
            TenantId = org.TenantId,
            Email = email,
            NormalizedEmail = email,
            RoleId = roleId,
            UserId = org.MemberId,
            CreatedByUserId = org.AdminId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(status == InvitationStatus.Expired ? -1 : 7),
            Status = status
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedJoinLinkAsync(Organization org, Guid roleId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        context.JoinLinks.Add(new JoinLink
        {
            TenantId = org.TenantId,
            RoleId = roleId,
            CreatedByUserId = org.AdminId,
            TokenHash = Guid.NewGuid().ToString("N"),
            Status = JoinLinkStatus.Active
        });
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = CreateBearerRequest(method, url, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return Client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("data");

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("errorCode").GetString();
}
