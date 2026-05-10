using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Integration tests verifying that system-level roles are resolved correctly.
/// System role assignments are found regardless of tenant context via GetSystemRolesForUserAsync.
/// Policy resolution for system roles works when the role is accessible in the current tenant.
/// </summary>
public sealed class SystemRoleResolutionTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task GetUserPermissionsAsync_SystemRole_PermissionsIncludedInResolution()
    {
        // Arrange — system role in the same tenant, permissions should be resolved
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "sysrole@test.com");

        var permId = await SeedPermissionAsync($"system.admin.{Guid.NewGuid():N}", "System Admin", "system");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var systemRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "SuperAdmin", null, RoleType.System, tenantId, false));
        systemRole.Success.Should().BeTrue();

        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "System Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(systemRole.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, systemRole.Data!.Id, tenantId));

        // Act — resolve permissions
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — system role permissions are included
        permissions.Should().Contain(p => p.StartsWith("system.admin."));
    }

    [Fact]
    public async Task GetUserPermissionsAsync_SystemAndTenantRoles_UnionedCorrectly()
    {
        // Arrange — user has both a system role and a tenant-scoped role in the same tenant
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "both@test.com");

        var sysPerm = await SeedPermissionAsync($"sys.perm.{Guid.NewGuid():N}", "System Perm", "system");
        var tenantPerm = await SeedPermissionAsync($"tenant.perm.{Guid.NewGuid():N}", "Tenant Perm", "tenant");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // System role with its own policy/permission
        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "SysRole", null, RoleType.System, tenantId, false));
        var sysPolicy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Sys Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(sysPolicy.Data!.Id, sysPerm);
        await roleRepo.AssignPolicyAsync(sysRole.Data!.Id, sysPolicy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));

        // Tenant-scoped role with its own policy/permission
        var tenantRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "TenantRole", null, RoleType.Application, tenantId, false));
        var tenantPolicy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Tenant Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(tenantPolicy.Data!.Id, tenantPerm);
        await roleRepo.AssignPolicyAsync(tenantRole.Data!.Id, tenantPolicy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, tenantRole.Data!.Id, tenantId));

        // Act
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — both system and tenant permissions present
        permissions.Should().HaveCount(2);
        permissions.Should().Contain(p => p.StartsWith("sys.perm."));
        permissions.Should().Contain(p => p.StartsWith("tenant.perm."));
    }

    [Fact]
    public async Task GetUserPermissionsAsync_OnlySystemRoles_NoTenantScopedRoles_StillGetsPermissions()
    {
        // Arrange — user has only a system role (no application roles) in the tenant
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "sysonly@test.com");

        var permId = await SeedPermissionAsync($"global.access.{Guid.NewGuid():N}", "Global Access", "global");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // Only a system role — no application roles
        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "GlobalAdmin", null, RoleType.System, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Global Policy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(sysRole.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));

        // Act — resolve permissions (user has no tenant-scoped roles)
        var permissionService = CreatePermissionService(tenantId);
        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — system role permissions still resolved
        permissions.Should().NotBeEmpty();
        permissions.Should().Contain(p => p.StartsWith("global.access."));
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_FoundRegardlessOfTenantContext()
    {
        // Arrange — system role assigned in tenant A, GetSystemRolesForUserAsync called from tenant B
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "cross-tenant@test.com");

        var roleRepoA = CreateRoleRepository(tenantAId);
        var userRoleRepoA = CreateUserRoleRepository(tenantAId);

        var sysRole = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "CrossTenantAdmin", null, RoleType.System, tenantAId, false));
        await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantAId));

        // Act — query system roles from tenant B context
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        var result = await userRoleRepoB.GetSystemRolesForUserAsync(userId);

        // Assert — system role is found regardless of tenant context
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].RoleName.Should().Be("CrossTenantAdmin");
    }

    [Fact]
    public async Task HasSystemRoleAsync_UserHasSystemRole_ReturnsTrue()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "hassysrole@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "PlatformAdmin", null, RoleType.System, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));

        // Act
        var permissionService = CreatePermissionService(tenantId);
        var result = await permissionService.HasSystemRoleAsync(userId, "PlatformAdmin");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasSystemRoleAsync_CaseInsensitive_ReturnsTrue()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "casetest@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "SuperAdmin", null, RoleType.System, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));

        // Act — check with different casing
        var permissionService = CreatePermissionService(tenantId);
        var result = await permissionService.HasSystemRoleAsync(userId, "superadmin");

        // Assert
        result.Should().BeTrue();
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
