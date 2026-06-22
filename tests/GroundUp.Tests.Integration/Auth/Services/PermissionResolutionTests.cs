using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Integration tests verifying end-to-end permission resolution through the full
/// hierarchy: User → UserRole → Role → RolePolicy → Policy → PolicyPermission → Permission.
/// Uses a real Postgres database via Testcontainers.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PermissionResolutionTests : AuthIntegrationTestBase
{
    public PermissionResolutionTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetUserPermissionsAsync_FullHierarchy_ReturnsCorrectPermissions()
    {
        // Arrange — seed a full permission hierarchy
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "user@test.com");

        var perm1Id = await SeedPermissionAsync("orders.read", "Read Orders", "orders");
        var perm2Id = await SeedPermissionAsync("orders.write", "Write Orders", "orders");
        var perm3Id = await SeedPermissionAsync("users.read", "Read Users", "users");

        // Create role and policy
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Editor", null, RoleType.Application, tenantId, false));
        role.Success.Should().BeTrue();

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Editor Policy", null, tenantId));
        policy.Success.Should().BeTrue();

        // Assign permissions to policy
        (await policyRepo.AssignPermissionAsync(policy.Data!.Id, perm1Id)).Success.Should().BeTrue();
        (await policyRepo.AssignPermissionAsync(policy.Data!.Id, perm2Id)).Success.Should().BeTrue();
        (await policyRepo.AssignPermissionAsync(policy.Data!.Id, perm3Id)).Success.Should().BeTrue();

        // Assign policy to role
        (await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id)).Success.Should().BeTrue();

        // Assign role to user
        var userRole = await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));
        userRole.Success.Should().BeTrue();

        // Act — resolve permissions via PermissionService
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert
        permissions.Should().HaveCount(3);
        permissions.Should().Contain("orders.read");
        permissions.Should().Contain("orders.write");
        permissions.Should().Contain("users.read");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_MultipleRolesOverlappingPermissions_ReturnsDeduplicatedSet()
    {
        // Arrange — two roles granting overlapping permissions
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "overlap@test.com");

        var permReadId = await SeedPermissionAsync($"data.read.{Guid.NewGuid():N}", "Read Data", "data");
        var permWriteId = await SeedPermissionAsync($"data.write.{Guid.NewGuid():N}", "Write Data", "data");
        var permDeleteId = await SeedPermissionAsync($"data.delete.{Guid.NewGuid():N}", "Delete Data", "data");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // Role 1: read + write
        var role1 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Reader-Writer", null, RoleType.Application, tenantId, false));
        var policy1 = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "RW Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy1.Data!.Id, permReadId);
        await policyRepo.AssignPermissionAsync(policy1.Data!.Id, permWriteId);
        await roleRepo.AssignPolicyAsync(role1.Data!.Id, policy1.Data!.Id);

        // Role 2: read + delete (overlaps on read)
        var role2 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Reader-Deleter", null, RoleType.Application, tenantId, false));
        var policy2 = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "RD Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy2.Data!.Id, permReadId);
        await policyRepo.AssignPermissionAsync(policy2.Data!.Id, permDeleteId);
        await roleRepo.AssignPolicyAsync(role2.Data!.Id, policy2.Data!.Id);

        // Assign both roles to user
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role1.Data!.Id, tenantId));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role2.Data!.Id, tenantId));

        // Act
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — deduplicated: read appears once
        permissions.Should().HaveCount(3);
        permissions.Should().Contain(p => p.StartsWith("data.read."));
        permissions.Should().Contain(p => p.StartsWith("data.write."));
        permissions.Should().Contain(p => p.StartsWith("data.delete."));
    }

    [Fact]
    public async Task HasPermissionAsync_UserHasPermission_ReturnsTrue()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "has-perm@test.com");

        var permId = await SeedPermissionAsync($"check.perm.{Guid.NewGuid():N}", "Check Perm", "check");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Checker", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Check Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        // Act
        var permissionService = CreatePermissionService(tenantId);
        var permKey = (await DbContext.Permissions.FindAsync(permId))!.Key;
        var result = await permissionService.HasPermissionAsync(userId, permKey);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_UserLacksPermission_ReturnsFalse()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "no-perm@test.com");

        // Act — user has no roles at all
        var permissionService = CreatePermissionService(tenantId);
        var result = await permissionService.HasPermissionAsync(userId, "nonexistent.permission");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_UserWithNoRoles_ReturnsEmptySet()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "noroles@test.com");

        // Act
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert
        permissions.Should().BeEmpty();
    }

    /// <summary>
    /// Creates a PermissionService with real repositories pointing at the test database.
    /// </summary>
    private PermissionService CreatePermissionService(Guid tenantId)
    {
        var tenantContext = CreateTenantContext(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);
        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });

        return new PermissionService(userRoleRepo, roleRepo, policyRepo, tenantContext, cache, options);
    }
}

