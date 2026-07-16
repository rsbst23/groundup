using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Services;
using GroundUp.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// Integration tests for TenantAdmin permission bypass and last-admin guard.
/// Validates Requirements 15.13 and 15.14 — TenantAdmin bypasses all permission checks
/// within the same tenant (but not cross-tenant), and the LastAdminGuard prevents removal
/// of the sole TenantAdmin from a tenant.
/// </summary>
[Collection("AuthFlow")]
public sealed class TenantAdminIntegrationTests : AuthFlowTestBase
{
    public TenantAdminIntegrationTests(AuthFlowTestFixture fixture) : base(fixture)
    {
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: TenantAdmin permission bypass (same tenant)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_InSameTenant_BypassesAllPermissionChecks()
    {
        // Arrange — seed tenant, user, membership, TenantAdmin role, and role assignment
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Admin Corp", $"admin-corp-{suffix}");
        var externalSub = $"kc-admin-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"admin-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        // Create TenantAdmin role scoped to this tenant
        var tenantAdminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, tenantAdminRoleId, tenantId);

        // Act — resolve IPermissionService from DI with TenantContext set to tenantId
        using var scope = Fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.TenantId = tenantId;

        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        // Check an arbitrary permission that the user does NOT explicitly have
        var hasPermission = await permissionService.HasPermissionAsync(userId, "arbitrary.nonexistent.permission");

        // Assert — TenantAdmin bypass should return true for any permission in own tenant
        hasPermission.Should().BeTrue(
            "TenantAdmin in the same tenant should bypass all permission checks");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: TenantAdmin bypass does NOT apply cross-tenant
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantAdmin_InDifferentTenant_DoesNotBypassPermissionChecks()
    {
        // Arrange — seed two tenants; user has TenantAdmin in tenant A only
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{suffix}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{suffix}");
        var externalSub = $"kc-cross-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"cross-{suffix}@example.com");

        // Membership in both tenants
        await SeedMembershipAsync(userId, tenantAId);
        await SeedMembershipAsync(userId, tenantBId);

        // TenantAdmin role in tenant A only
        var tenantAdminRoleId = await SeedRoleAsync(tenantAId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, tenantAdminRoleId, tenantAId);

        // Act — resolve IPermissionService with TenantContext set to tenant B
        using var scope = Fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.TenantId = tenantBId;

        var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        // Check an arbitrary permission in tenant B where the user does NOT have TenantAdmin
        var hasPermission = await permissionService.HasPermissionAsync(userId, "arbitrary.nonexistent.permission");

        // Assert — TenantAdmin in tenant A should NOT grant bypass in tenant B
        hasPermission.Should().BeFalse(
            "TenantAdmin in one tenant must not bypass permission checks in a different tenant");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: Last-admin guard rejection (sole TenantAdmin)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LastAdminGuard_SoleTenantAdmin_RejectsRemoval()
    {
        // Arrange — seed a tenant with exactly one TenantAdmin
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Solo Admin Corp", $"solo-admin-{suffix}");
        var externalSub = $"kc-solo-{suffix}";
        var userId = await SeedUserAsync(externalSub, $"solo-{suffix}@example.com");
        await SeedMembershipAsync(userId, tenantId);

        var tenantAdminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId, tenantAdminRoleId, tenantId);

        // Act — resolve LastAdminGuard from DI and attempt removal
        using var scope = Fixture.Services.CreateScope();
        var lastAdminGuard = scope.ServiceProvider.GetRequiredService<LastAdminGuard>();

        var result = await lastAdminGuard.CanRemoveTenantAdminAsync(userId, tenantId);

        // Assert — should reject removal (LAST_ADMIN, 409)
        result.Success.Should().BeFalse("removing the last TenantAdmin should be rejected");
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("LAST_ADMIN");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4: Last-admin guard allows removal when multiple admins exist
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LastAdminGuard_MultipleTenantAdmins_AllowsRemoval()
    {
        // Arrange — seed a tenant with two users who both have TenantAdmin
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await SeedTenantAsync("Multi Admin Corp", $"multi-admin-{suffix}");

        var externalSub1 = $"kc-multi1-{suffix}";
        var userId1 = await SeedUserAsync(externalSub1, $"multi1-{suffix}@example.com", "Admin One");
        await SeedMembershipAsync(userId1, tenantId);

        var externalSub2 = $"kc-multi2-{suffix}";
        var userId2 = await SeedUserAsync(externalSub2, $"multi2-{suffix}@example.com", "Admin Two");
        await SeedMembershipAsync(userId2, tenantId);

        var tenantAdminRoleId = await SeedRoleAsync(tenantId, AuthRoleNames.TenantAdmin, isSystem: true);
        await AssignRoleToUserAsync(userId1, tenantAdminRoleId, tenantId);
        await AssignRoleToUserAsync(userId2, tenantAdminRoleId, tenantId);

        // Act — attempt to remove one of the two TenantAdmins
        using var scope = Fixture.Services.CreateScope();
        var lastAdminGuard = scope.ServiceProvider.GetRequiredService<LastAdminGuard>();

        var result = await lastAdminGuard.CanRemoveTenantAdminAsync(userId1, tenantId);

        // Assert — should allow removal since there are 2+ admins
        result.Success.Should().BeTrue("removal should succeed when multiple TenantAdmins exist");
        result.Data.Should().BeTrue();
    }
}
