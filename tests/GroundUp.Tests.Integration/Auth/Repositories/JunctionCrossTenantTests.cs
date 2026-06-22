using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that junction operations (RolePolicy, PolicyPermission)
/// cannot cross tenant boundaries. Assigning a policy from Tenant B to a role in Tenant A
/// must fail, and querying junction data for entities in other tenants must return NotFound.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
[Collection("AuthPostgres")]
public sealed class JunctionCrossTenantTests : AuthIntegrationTestBase
{
    public JunctionCrossTenantTests(AuthPostgresFixture fixture) : base(fixture) { }

    #region RolePolicy Cross-Tenant

    [Fact]
    public async Task AssignPolicy_RoleInTenantA_PolicyInTenantB_ReturnsNotFound()
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
    public async Task AssignPolicy_RoleInTenantA_PolicyInTenantA_Succeeds()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");

        var roleRepo = CreateRoleRepository(tenantAId);
        var policyRepo = CreatePolicyRepository(tenantAId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Policy A", null, tenantAId));

        // Act — assign within same tenant
        var result = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RemovePolicy_RoleInTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var roleRepoB = CreateRoleRepository(tenantBId);
        var policyRepoB = CreatePolicyRepository(tenantBId);

        var role = await roleRepoB.AddAsync(new RoleDto(Guid.Empty, "Role B", null, RoleType.Application, tenantBId, false));
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));
        await roleRepoB.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Act — try to remove from Tenant A's context
        var roleRepoA = CreateRoleRepository(tenantAId);
        var result = await roleRepoA.RemovePolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetPoliciesForRole_RoleInTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var roleRepoB = CreateRoleRepository(tenantBId);
        var policyRepoB = CreatePolicyRepository(tenantBId);

        var role = await roleRepoB.AddAsync(new RoleDto(Guid.Empty, "Role B", null, RoleType.Application, tenantBId, false));
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));
        await roleRepoB.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Act — query from Tenant A's context
        var roleRepoA = CreateRoleRepository(tenantAId);
        var result = await roleRepoA.GetPoliciesForRoleAsync(role.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    #endregion

    #region PolicyPermission Cross-Tenant

    [Fact]
    public async Task AssignPermission_PolicyInTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var policyRepoB = CreatePolicyRepository(tenantBId);
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));

        var permissionId = await SeedPermissionAsync($"perm.{Guid.NewGuid():N}", "Test Perm", "test");

        // Act — try to assign permission to a policy from Tenant A's context
        var policyRepoA = CreatePolicyRepository(tenantAId);
        var result = await policyRepoA.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RemovePermission_PolicyInTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var policyRepoB = CreatePolicyRepository(tenantBId);
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));

        var permissionId = await SeedPermissionAsync($"perm.{Guid.NewGuid():N}", "Test Perm", "test");
        await policyRepoB.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Act — try to remove from Tenant A's context
        var policyRepoA = CreatePolicyRepository(tenantAId);
        var result = await policyRepoA.RemovePermissionAsync(policy.Data!.Id, permissionId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetPermissionsForPolicy_PolicyInTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");

        var policyRepoB = CreatePolicyRepository(tenantBId);
        var policy = await policyRepoB.AddAsync(new PolicyDto(Guid.Empty, "Policy B", null, tenantBId));

        var permissionId = await SeedPermissionAsync($"perm.{Guid.NewGuid():N}", "Test Perm", "test");
        await policyRepoB.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Act — query from Tenant A's context
        var policyRepoA = CreatePolicyRepository(tenantAId);
        var result = await policyRepoA.GetPermissionsForPolicyAsync(policy.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    #endregion

    #region Idempotency

    [Fact]
    public async Task AssignPolicy_SamePolicyTwice_IsIdempotent()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");

        var roleRepo = CreateRoleRepository(tenantAId);
        var policyRepo = CreatePolicyRepository(tenantAId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Policy A", null, tenantAId));

        // Act — assign the same policy twice
        var firstResult = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        var secondResult = await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);

        // Assert — both calls succeed (idempotent)
        firstResult.Success.Should().BeTrue();
        secondResult.Success.Should().BeTrue();

        // Verify only one entry exists
        var policies = await roleRepo.GetPoliciesForRoleAsync(role.Data!.Id);
        policies.Success.Should().BeTrue();
        policies.Data!.Should().ContainSingle();
        policies.Data!.First().Id.Should().Be(policy.Data!.Id);
    }

    [Fact]
    public async Task AssignPermission_SamePermissionTwice_IsIdempotent()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");

        var policyRepo = CreatePolicyRepository(tenantAId);
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Policy A", null, tenantAId));

        var permissionId = await SeedPermissionAsync($"perm.{Guid.NewGuid():N}", "Test Perm", "test");

        // Act — assign the same permission twice
        var firstResult = await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);
        var secondResult = await policyRepo.AssignPermissionAsync(policy.Data!.Id, permissionId);

        // Assert — both calls succeed (idempotent)
        firstResult.Success.Should().BeTrue();
        secondResult.Success.Should().BeTrue();

        // Verify only one entry exists
        var permissions = await policyRepo.GetPermissionsForPolicyAsync(policy.Data!.Id);
        permissions.Success.Should().BeTrue();
        permissions.Data!.Should().ContainSingle();
        permissions.Data!.First().Id.Should().Be(permissionId);
    }

    #endregion
}

