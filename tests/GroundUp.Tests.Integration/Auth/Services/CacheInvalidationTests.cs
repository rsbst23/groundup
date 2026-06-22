using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Auth.Services.EventHandlers;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GroundUp.Tests.Integration.Auth.Services;

/// <summary>
/// Integration tests verifying that cache invalidation works correctly when
/// role assignments change. Resolves permissions (populates cache), modifies
/// data, publishes events, and verifies re-resolution produces updated results.
/// </summary>
[Collection("AuthPostgres")]
public sealed class CacheInvalidationTests : AuthIntegrationTestBase
{
    public CacheInvalidationTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CacheInvalidation_NewUserRoleAdded_ReResolutionReflectsNewPermissions()
    {
        // Arrange — seed initial data and resolve permissions (populates cache)
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "cache-test@test.com");

        var perm1Id = await SeedPermissionAsync($"cache.perm1.{Guid.NewGuid():N}", "Perm 1", "cache");
        var perm2Id = await SeedPermissionAsync($"cache.perm2.{Guid.NewGuid():N}", "Perm 2", "cache");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // Role 1 with perm1
        var role1 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Role1", null, RoleType.Application, tenantId, false));
        var policy1 = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Policy1", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy1.Data!.Id, perm1Id);
        await roleRepo.AssignPolicyAsync(role1.Data!.Id, policy1.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role1.Data!.Id, tenantId));

        // Role 2 with perm2 (not yet assigned to user)
        var role2 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Role2", null, RoleType.Application, tenantId, false));
        var policy2 = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "Policy2", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy2.Data!.Id, perm2Id);
        await roleRepo.AssignPolicyAsync(role2.Data!.Id, policy2.Data!.Id);

        // Create shared cache and permission service
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tenantContext = CreateTenantContext(tenantId);
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });
        var permissionService = new PermissionService(userRoleRepo, roleRepo, policyRepo, tenantContext, cache, options);

        // First resolution — populates cache with only perm1
        var initialPermissions = await permissionService.GetUserPermissionsAsync(userId);
        initialPermissions.Should().HaveCount(1);

        // Act — add role2 to user via repository
        var newUserRole = await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role2.Data!.Id, tenantId));
        newUserRole.Success.Should().BeTrue();

        // Publish EntityCreatedEvent<UserRoleDto> to trigger cache invalidation
        var handler = new UserRoleChangedHandler(cache, NullLogger<UserRoleChangedHandler>.Instance);
        await handler.HandleAsync(new EntityCreatedEvent<UserRoleDto>
        {
            Entity = newUserRole.Data!
        });

        // Re-resolve — should reflect the new role's permissions
        var updatedPermissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert
        updatedPermissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task CacheInvalidation_WithoutEvent_CacheReturnsStaleData()
    {
        // Arrange — seed data and resolve (populates cache)
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "stale@test.com");

        var permId = await SeedPermissionAsync($"stale.perm.{Guid.NewGuid():N}", "Stale Perm", "stale");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var role1 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "StaleRole", null, RoleType.Application, tenantId, false));
        var policy1 = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "StalePolicy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy1.Data!.Id, permId);
        await roleRepo.AssignPolicyAsync(role1.Data!.Id, policy1.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role1.Data!.Id, tenantId));

        // Create shared cache and permission service
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tenantContext = CreateTenantContext(tenantId);
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });
        var permissionService = new PermissionService(userRoleRepo, roleRepo, policyRepo, tenantContext, cache, options);

        // First resolution — populates cache
        var initialPermissions = await permissionService.GetUserPermissionsAsync(userId);
        initialPermissions.Should().HaveCount(1);

        // Add a new permission to the policy (without publishing event)
        var newPermId = await SeedPermissionAsync($"new.perm.{Guid.NewGuid():N}", "New Perm", "new");
        await policyRepo.AssignPermissionAsync(policy1.Data!.Id, newPermId);

        // Act — resolve again WITHOUT invalidating cache
        var cachedPermissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — still returns stale data (only 1 permission)
        cachedPermissions.Should().HaveCount(1);
    }

    [Fact]
    public async Task CacheInvalidation_ManualEviction_ReResolutionReflectsChanges()
    {
        // Arrange — demonstrates that manual cache eviction (simulating what a handler would do)
        // causes re-resolution to pick up new data
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "manual-evict@test.com");

        var perm1Id = await SeedPermissionAsync($"evict.perm1.{Guid.NewGuid():N}", "Evict Perm 1", "evict");
        var perm2Id = await SeedPermissionAsync($"evict.perm2.{Guid.NewGuid():N}", "Evict Perm 2", "evict");

        var roleRepo = CreateRoleRepository(tenantId);
        var policyRepo = CreatePolicyRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // Role with policy containing perm1
        var role = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "EvictRole", null, RoleType.Application, tenantId, false));
        var policy = await policyRepo.AddAsync(new PolicyDto(Guid.Empty, "EvictPolicy", null, tenantId));
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, perm1Id);
        await roleRepo.AssignPolicyAsync(role.Data!.Id, policy.Data!.Id);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        // Create shared cache and permission service
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tenantContext = CreateTenantContext(tenantId);
        var options = Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 });
        var permissionService = new PermissionService(userRoleRepo, roleRepo, policyRepo, tenantContext, cache, options);

        // First resolution — populates cache with perm1
        var initialPermissions = await permissionService.GetUserPermissionsAsync(userId);
        initialPermissions.Should().HaveCount(1);

        // Add perm2 to the policy
        await policyRepo.AssignPermissionAsync(policy.Data!.Id, perm2Id);

        // Act — manually evict the cache key (simulating what a handler does)
        var cacheKey = $"permissions:{userId}:{tenantId}";
        cache.Remove(cacheKey);

        // Re-resolve
        var updatedPermissions = await permissionService.GetUserPermissionsAsync(userId);

        // Assert — now has both permissions
        updatedPermissions.Should().HaveCount(2);
    }
}

