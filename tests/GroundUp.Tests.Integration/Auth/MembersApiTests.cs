using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// GU-68 Requirement 5: the member administration endpoints (<c>/api/members</c>) through the sample
/// app, with real permissions, role rules and token revocation.
/// </summary>
[Collection("AuthFlow")]
public sealed class MembersApiTests : AuthFlowTestBase
{
    public MembersApiTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task List_AsTenantAdmin_ReturnsActiveAndPendingMembersWithRoleSources()
    {
        var org = await SeedOrganizationAsync();
        var pendingId = await SeedPendingMemberAsync(org.TenantId, $"pending-{org.Suffix}@example.com");
        var formerId = await SeedUserAsync($"kc-former-{org.Suffix}", $"former-{org.Suffix}@example.com");
        await SeedMembershipAsync(formerId, org.TenantId, isActive: false);

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/members?pageSize=50", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Total-Count").Single().Should().Be("3");
        var items = (await DataAsync(response)).GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(i => Guid.Parse(i.GetProperty("userId").GetString()!)).ToList();
        ids.Should().BeEquivalentTo(new[] { org.AdminId, org.MemberId, pendingId }, "an inactive membership is not listed");

        var admin = items.Single(i => Guid.Parse(i.GetProperty("userId").GetString()!) == org.AdminId);
        var adminRole = admin.GetProperty("roles").EnumerateArray().Single();
        adminRole.GetProperty("roleName").GetString().Should().Be(AuthRoleNames.TenantAdmin);
        adminRole.GetProperty("sources").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task List_AsPlainMember_Forbidden()
    {
        var org = await SeedOrganizationAsync();

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/members", org.MemberToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignRole_AsTenantAdmin_SavesAManualAssignment()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/members/{org.MemberId}/roles/{editorId}", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var context = Fixture.CreateAuthDbContext();
        var row = await context.UserRoles.AsNoTracking().SingleAsync(ur => ur.UserId == org.MemberId && ur.RoleId == editorId);
        row.Source.Should().Be(RoleAssignmentSource.Manual);
    }

    [Fact]
    public async Task AssignRole_AsPlainMember_Forbidden()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/members/{org.MemberId}/roles/{editorId}", org.MemberToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveRole_LastTenantAdmin_Conflict()
    {
        var org = await SeedOrganizationAsync();

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Delete, $"/api/members/{org.AdminId}/roles/{org.AdminRoleId}", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).GetProperty("errorCode").GetString().Should().Be(AuthErrorCodes.LastAdmin);
    }

    [Fact]
    public async Task Remove_AsTenantAdmin_DeactivatesTheMemberAndRevokesTheirToken()
    {
        var org = await SeedOrganizationAsync();
        var editorId = await SeedRoleAsync(org.TenantId, "Editor");
        await AssignRoleToUserAsync(org.MemberId, editorId, org.TenantId);
        (await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/auth/me", org.MemberToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the member's token works before the removal");

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Delete, $"/api/members/{org.MemberId}", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(org.MemberId, org.TenantId)).Should().Be(MembershipStatus.Inactive);
        await using (var context = Fixture.CreateAuthDbContext())
        {
            (await context.UserRoles.CountAsync(ur => ur.UserId == org.MemberId && ur.TenantId == org.TenantId)).Should().Be(0);
        }

        (await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/auth/me", org.MemberToken)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "tokens issued before the removal are revoked");
    }

    [Fact]
    public async Task Remove_PendingMember_DeactivatesIt()
    {
        var org = await SeedOrganizationAsync();
        var pendingId = await SeedPendingMemberAsync(org.TenantId, $"pending-{org.Suffix}@example.com");

        var response = await Client.SendAsync(CreateBearerRequest(HttpMethod.Delete, $"/api/members/{pendingId}", org.AdminToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(pendingId, org.TenantId)).Should().Be(MembershipStatus.Inactive);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private sealed record Organization(
        Guid TenantId, Guid AdminId, Guid AdminRoleId, string AdminToken, Guid MemberId, string MemberToken, string Suffix);

    /// <summary>A tenant with a TenantAdmin and a plain member, each with a token for the tenant.</summary>
    private async Task<Organization> SeedOrganizationAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Members Org {suffix}", $"members-org-{suffix}");
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);

        var adminId = await SeedUserAsync($"kc-admin-{suffix}", $"admin-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);

        var memberId = await SeedUserAsync($"kc-member-{suffix}", $"member-{suffix}@example.com");
        await SeedMembershipAsync(memberId, tenantId);

        return new Organization(
            tenantId, adminId, adminRoleId, Fixture.GenerateTestToken(adminId, tenantId),
            memberId, Fixture.GenerateTestToken(memberId, tenantId), suffix);
    }

    private async Task<Guid> SeedPendingMemberAsync(Guid tenantId, string email)
    {
        await using var context = Fixture.CreateAuthDbContext();
        var user = new GroundUp.Auth.Core.Entities.User { ExternalUserId = null, Email = email, DisplayName = email, IsActive = true };
        context.Users.Add(user);
        context.UserTenants.Add(new GroundUp.Auth.Core.Entities.UserTenant
        {
            UserId = user.Id,
            TenantId = tenantId,
            ExternalUserId = null,
            Status = MembershipStatus.Pending
        });
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<MembershipStatus> StatusOfAsync(Guid userId, Guid tenantId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.UserTenants.AsNoTracking()
            .Where(ut => ut.UserId == userId && ut.TenantId == tenantId)
            .Select(ut => ut.Status)
            .SingleAsync();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
        => (await BodyAsync(response)).GetProperty("data");
}
