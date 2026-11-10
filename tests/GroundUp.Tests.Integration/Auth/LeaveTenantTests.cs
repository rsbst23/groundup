using System.Net;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for <c>POST /auth/tenants/{tenantId}/leave</c>.
/// Validates Requirements 5.1, 5.2 — membership deactivation, last-admin guard,
/// token reissuance when leaving the active tenant, and the no-tenants-remain path.
/// </summary>
[Collection("AuthFlow")]
public sealed class LeaveTenantTests : AuthFlowTestBase
{
    public LeaveTenantTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Unauthenticated → 401
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_Unauthenticated_Returns401()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/auth/tenants/{Guid.NewGuid()}/leave");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Happy path — leave a tenant (not the current one)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_NonCurrentTenant_DeactivatesMembership_Returns200()
    {
        // Arrange — user is a member of two tenants; leaves the non-current one
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var currentTenantId = await SeedTenantAsync($"Current {suffix}", $"current-{suffix}");
        var otherTenantId = await SeedTenantAsync($"Other {suffix}", $"other-{suffix}");
        var userId = await SeedUserAsync($"sub-leave-{suffix}", $"leave-{suffix}@example.com");
        await SeedMembershipAsync(userId, currentTenantId);
        await SeedMembershipAsync(userId, otherTenantId);

        // Token scoped to currentTenantId
        var token = Fixture.GenerateTestToken(userId, currentTenantId);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{otherTenantId}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with { success: true }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // Assert — other tenant membership is now inactive
        var memberships = await GetAllMembershipsForUserAsync(userId);
        var leftMembership = memberships.First(m => m.TenantId == otherTenantId);
        leftMembership.IsActive.Should().BeFalse("the membership must be deactivated after leave");

        var currentMembership = memberships.First(m => m.TenantId == currentTenantId);
        currentMembership.IsActive.Should().BeTrue("the current tenant membership must remain active");

        // Assert — no new cookie was written (user did not leave current tenant)
        var newCookie = ExtractAuthCookie(response);
        newCookie.Should().BeNull("no cookie reissuance is needed when leaving a non-current tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Leave current tenant — switches to another available tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_CurrentTenant_ReissuesTokenForNextTenant()
    {
        // Arrange — user views tenant1, leaves it, and has tenant2 remaining
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant1Id = await SeedTenantAsync($"Leave T1 {suffix}", $"leave-t1-{suffix}");
        var tenant2Id = await SeedTenantAsync($"Remain T2 {suffix}", $"remain-t2-{suffix}");
        var userId = await SeedUserAsync($"sub-switch-{suffix}", $"switch-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenant1Id);
        await SeedMembershipAsync(userId, tenant2Id);

        // Token scoped to tenant1 (the one being left)
        var token = Fixture.GenerateTestToken(userId, tenant1Id);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenant1Id}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with { success: true }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        // Assert — tenant1 membership deactivated
        var memberships = await GetAllMembershipsForUserAsync(userId);
        memberships.First(m => m.TenantId == tenant1Id).IsActive
            .Should().BeFalse();
        memberships.First(m => m.TenantId == tenant2Id).IsActive
            .Should().BeTrue();

        // Assert — a new auth cookie was written (token reissued for tenant2)
        var newCookie = ExtractAuthCookie(response);
        newCookie.Should().NotBeNullOrEmpty(
            "leaving the current tenant should trigger token reissuance for the next available tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Leave last tenant — clears cookie and returns { noTenants: true }
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_LastTenant_ClearsCookieAndReturnsNoTenants()
    {
        // Arrange — user has only one active membership
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Last Tenant {suffix}", $"last-{suffix}");
        var userId = await SeedUserAsync($"sub-last-{suffix}", $"last-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Token scoped to the only tenant
        var token = Fixture.GenerateTestToken(userId, tenantId);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 with { noTenants: true }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("noTenants", out var noTenantsEl).Should().BeTrue();
        noTenantsEl.GetBoolean().Should().BeTrue();

        // Assert — membership is deactivated
        var memberships = await GetAllMembershipsForUserAsync(userId);
        memberships.First(m => m.TenantId == tenantId).IsActive
            .Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Not a member → 404
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_NotAMember_Returns404()
    {
        // Arrange — user has no membership in the specified tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ownTenantId = await SeedTenantAsync($"Own Tenant {suffix}", $"own-{suffix}");
        var otherTenantId = await SeedTenantAsync($"Other NM {suffix}", $"other-nm-{suffix}");
        var userId = await SeedUserAsync($"sub-nm-{suffix}", $"nm-{suffix}@example.com");
        await SeedMembershipAsync(userId, ownTenantId);
        // No membership in otherTenantId

        var token = Fixture.GenerateTestToken(userId, ownTenantId);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{otherTenantId}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 404 Not Found
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 6. Last admin guard — sole TenantAdmin cannot leave
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_SoleTenantAdmin_Returns403Forbidden()
    {
        // Arrange — user is the sole TenantAdmin in the tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Admin Tenant {suffix}", $"admin-{suffix}");
        var userId = await SeedUserAsync($"sub-admin-{suffix}", $"admin-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Seed the TenantAdmin system role and assign it to the user
        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, adminRoleId, tenantId);

        var token = Fixture.GenerateTestToken(userId, tenantId);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 403 Forbidden (last-admin guard)
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Assert — membership was NOT deactivated
        var memberships = await GetAllMembershipsForUserAsync(userId);
        memberships.First(m => m.TenantId == tenantId).IsActive
            .Should().BeTrue("the last admin's membership must NOT be deactivated");
    }

    // ────────────────────────────────────────────────────────────────────────
    // 7. Two admins — first admin CAN leave (guard does not block)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_TwoAdmins_FirstAdminCanLeave()
    {
        // Arrange — two TenantAdmin users in the same tenant
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Two Admins {suffix}", $"two-admins-{suffix}");
        var admin1Id = await SeedUserAsync($"sub-admin1-{suffix}", $"admin1-{suffix}@example.com");
        var admin2Id = await SeedUserAsync($"sub-admin2-{suffix}", $"admin2-{suffix}@example.com");
        await SeedMembershipAsync(admin1Id, tenantId);
        await SeedMembershipAsync(admin2Id, tenantId);

        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(admin1Id, adminRoleId, tenantId);
        await AssignRoleToUserAsync(admin2Id, adminRoleId, tenantId);

        // Token for admin1, leaving the tenant (admin2 still there)
        var token = Fixture.GenerateTestToken(admin1Id, tenantId);
        var request = CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", token);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — 200 (allowed because another admin exists)
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — admin1's membership deactivated; admin2's still active
        var admin1Memberships = await GetAllMembershipsForUserAsync(admin1Id);
        admin1Memberships.First(m => m.TenantId == tenantId).IsActive
            .Should().BeFalse();

        var admin2Memberships = await GetAllMembershipsForUserAsync(admin2Id);
        admin2Memberships.First(m => m.TenantId == tenantId).IsActive
            .Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 8. Two admins — after the first leaves, the second is the last admin
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTenant_SecondAdminAfterFirstLeft_Returns403BecauseOnlyOneActiveAdminRemains()
    {
        // Arrange — two TenantAdmins; leaving keeps the departed admin's role row in the database
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"Departing Admins {suffix}", $"departing-admins-{suffix}");
        var admin1Id = await SeedUserAsync($"sub-dep1-{suffix}", $"dep1-{suffix}@example.com");
        var admin2Id = await SeedUserAsync($"sub-dep2-{suffix}", $"dep2-{suffix}@example.com");
        await SeedMembershipAsync(admin1Id, tenantId);
        await SeedMembershipAsync(admin2Id, tenantId);

        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(admin1Id, adminRoleId, tenantId);
        await AssignRoleToUserAsync(admin2Id, adminRoleId, tenantId);

        var firstLeave = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", Fixture.GenerateTestToken(admin1Id, tenantId)));
        firstLeave.StatusCode.Should().Be(HttpStatusCode.OK, "a second active admin remains");

        // Act — the remaining admin tries to leave
        var secondLeave = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", Fixture.GenerateTestToken(admin2Id, tenantId)));

        // Assert — the departed admin must not count; the tenant would otherwise have no admin
        secondLeave.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin2Memberships = await GetAllMembershipsForUserAsync(admin2Id);
        admin2Memberships.First(m => m.TenantId == tenantId).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task LeaveTenant_RegularMemberOfTenantWithOneAdmin_Returns200()
    {
        // Arrange — the normal case: one admin and a regular member who wants to leave
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync($"One Admin {suffix}", $"one-admin-{suffix}");
        var adminId = await SeedUserAsync($"sub-oa-admin-{suffix}", $"oa-admin-{suffix}@example.com");
        var memberId = await SeedUserAsync($"sub-oa-member-{suffix}", $"oa-member-{suffix}@example.com");
        await SeedMembershipAsync(adminId, tenantId);
        await SeedMembershipAsync(memberId, tenantId);

        var adminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(adminId, adminRoleId, tenantId);

        // Act
        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/auth/tenants/{tenantId}/leave", Fixture.GenerateTestToken(memberId, tenantId)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var memberships = await GetAllMembershipsForUserAsync(memberId);
        memberships.First(m => m.TenantId == tenantId).IsActive.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helper: gets ALL memberships (active and inactive) for assertion
    // ────────────────────────────────────────────────────────────────────────

    private async Task<List<GroundUp.Auth.Core.Entities.UserTenant>> GetAllMembershipsForUserAsync(Guid userId)
    {
        await using var context = Fixture.CreateAuthDbContext();
        return await context.Set<GroundUp.Auth.Core.Entities.UserTenant>()
            .Where(ut => ut.UserId == userId)
            .ToListAsync();
    }
}
