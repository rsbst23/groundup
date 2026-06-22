using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying junction management (RolePolicy and PolicyPermission).
/// Property 8: RoleRepository junction management round-trip.
/// Property 9: PolicyRepository junction management round-trip.
/// Validates: Requirements 11.7, 11.8, 4.2, 4.3, 4.4, 5.2, 5.3, 5.4
/// </summary>
[Collection("AuthPostgres")]
public sealed class JunctionManagementTests : AuthIntegrationTestBase
{
    public JunctionManagementTests(AuthPostgresFixture fixture) : base(fixture) { }

    #region RolePolicy Junction (RoleRepository)

    [Fact]
    public async Task AssignPolicyAsync_CreatesRolePolicyRecord_GetPoliciesForRoleReturnsIt()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));

        // Act
        var assignResult = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        assignResult.Success.Should().BeTrue();

        var policies = await roleRepo.GetPoliciesForRoleAsync(role.Data!.Id);
        policies.Success.Should().BeTrue();
        policies.Data!.Should().HaveCount(1);
        policies.Data!.First().Id.Should().Be(policy.Data!.Id);
    }

    [Fact]
    public async Task AssignPolicyAsync_Twice_IsIdempotent_NoDuplicate()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));

        // Act
        var firstAssign = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        var secondAssign = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        firstAssign.Success.Should().BeTrue();
        secondAssign.Success.Should().BeTrue();

        var policies = await roleRepo.GetPoliciesForRoleAsync(role.Data!.Id);
        policies.Data!.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemovePolicyAsync_RemovesRecord_GetPoliciesForRoleNoLongerReturnsIt()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));

        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Act
        var removeResult = await roleRepo.RemovePolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        removeResult.Success.Should().BeTrue();

        var policies = await roleRepo.GetPoliciesForRoleAsync(role.Data!.Id);
        policies.Success.Should().BeTrue();
        policies.Data!.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePolicyAsync_ForNonExistentAssignment_ReturnsNotFound()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));

        // Act — never assigned, so removing should return NotFound
        var result = await roleRepo.RemovePolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignPolicyAsync_WithRoleFromTenantA_PolicyFromTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var roleRepoA = CreateRoleRepository(tenantAId);
        var policyRepoB = CreatePolicyRepository(tenantBId);

        var role = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));

        // Act — try to assign a policy from Tenant B to a role in Tenant A
        var result = await roleRepoA.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetPoliciesForRoleAsync_ForRoleInDifferentTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var roleRepoA = CreateRoleRepository(tenantAId);
        var roleRepoB = CreateRoleRepository(tenantBId);

        var role = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));

        // Act — query from Tenant B's context
        var result = await roleRepoB.GetPoliciesForRoleAsync(role.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    #endregion

    #region PolicyPermission Junction (PolicyRepository)

    [Fact]
    public async Task AssignPermissionAsync_CreatesPolicyPermissionRecord_GetPermissionsForPolicyReturnsIt()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var policyRepo = CreatePolicyRepository(tenantId);
        var permissionId = await SeedPermissionAsync($"settings.read.{Guid.NewGuid():N}", "Read Settings", "settings");

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantId));

        // Act
        var assignResult = await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        assignResult.Success.Should().BeTrue();

        var permissions = await policyRepo.GetPermissionsForPolicyAsync(policy.Data!.Id);
        permissions.Success.Should().BeTrue();
        permissions.Data!.Should().HaveCount(1);
        permissions.Data!.First().Id.Should().Be(permissionId);
    }

    [Fact]
    public async Task AssignPermissionAsync_Twice_IsIdempotent_NoDuplicate()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var policyRepo = CreatePolicyRepository(tenantId);
        var permissionId = await SeedPermissionAsync($"settings.write.{Guid.NewGuid():N}", "Write Settings", "settings");

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Write Policy", null, tenantId));

        // Act
        var firstAssign = await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);
        var secondAssign = await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        firstAssign.Success.Should().BeTrue();
        secondAssign.Success.Should().BeTrue();

        var permissions = await policyRepo.GetPermissionsForPolicyAsync(policy.Data!.Id);
        permissions.Data!.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemovePermissionAsync_RemovesRecord_GetPermissionsForPolicyNoLongerReturnsIt()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var policyRepo = CreatePolicyRepository(tenantId);
        var permissionId = await SeedPermissionAsync($"settings.delete.{Guid.NewGuid():N}", "Delete Settings", "settings");

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Delete Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Act
        var removeResult = await policyRepo.RemovePermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        removeResult.Success.Should().BeTrue();

        var permissions = await policyRepo.GetPermissionsForPolicyAsync(policy.Data!.Id);
        permissions.Success.Should().BeTrue();
        permissions.Data!.Should().BeEmpty();
    }

    [Fact]
    public async Task RemovePermissionAsync_ForNonExistentAssignment_ReturnsNotFound()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Tenant", $"tenant-{Guid.NewGuid():N}");
        var policyRepo = CreatePolicyRepository(tenantId);
        var permissionId = await SeedPermissionAsync($"settings.admin.{Guid.NewGuid():N}", "Admin Settings", "settings");

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Admin Policy", null, tenantId));

        // Act — never assigned, so removing should return NotFound
        var result = await policyRepo.RemovePermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    #endregion
}

