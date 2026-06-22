using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that UserRole data NEVER leaks across tenants.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
[Collection("AuthPostgres")]
public sealed class UserRoleTenantIsolationTests : AuthIntegrationTestBase
{
    public UserRoleTenantIsolationTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateUserRole_AsTenantA_GetByUserId_AsTenantA_ReturnsIt()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var roleRepo = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantAId, false));

        var userRoleRepo = CreateUserRoleRepository(tenantAId);
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, tenantAId));

        // Act
        var result = await userRoleRepo.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Should().ContainSingle();
        result.Data!.First().UserId.Should().Be(userId);
        result.Data!.First().RoleId.Should().Be(roleResult.Data!.Id);
    }

    [Fact]
    public async Task CreateUserRole_AsTenantA_GetByUserId_AsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var roleRepo = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantAId, false));

        var userRoleRepoA = CreateUserRoleRepository(tenantAId);
        await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, tenantAId));

        // Act — query from Tenant B's context
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        var result = await userRoleRepoB.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUserRoles_InBothTenants_GetByUserId_ReturnsOnlyCurrentTenant()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        // Create roles in both tenants
        var roleRepoA = CreateRoleRepository(tenantAId);
        var roleRepoB = CreateRoleRepository(tenantBId);
        var roleA = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "Admin A", null, RoleType.Application, tenantAId, false));
        var roleB = await roleRepoB.AddAsync(new RoleDto(Guid.Empty, "Admin B", null, RoleType.Application, tenantBId, false));

        // Assign user to roles in both tenants
        var userRoleRepoA = CreateUserRoleRepository(tenantAId);
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, roleA.Data!.Id, tenantAId));
        await userRoleRepoB.AddAsync(new UserRoleDto(Guid.Empty, userId, roleB.Data!.Id, tenantBId));

        // Act — query from Tenant A's context
        var resultA = await userRoleRepoA.GetByUserIdAsync(userId);

        // Assert — should only see Tenant A's role assignment
        resultA.Success.Should().BeTrue();
        resultA.Data!.Should().ContainSingle();
        resultA.Data!.First().RoleId.Should().Be(roleA.Data!.Id);
        resultA.Data!.First().TenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task UpdateUserRole_AsTenantB_ReturnsNotFound()
    {
        // Arrange — create a UserRole in Tenant A
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var roleRepo = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantAId, false));

        var userRoleRepoA = CreateUserRoleRepository(tenantAId);
        var createResult = await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, tenantAId));
        var userRoleId = createResult.Data!.Id;

        // Act — try to update from Tenant B's context
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        var updatedDto = new UserRoleDto(userRoleId, userId, roleResult.Data!.Id, tenantAId);
        var result = await userRoleRepoB.UpdateAsync(userRoleId, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteUserRole_AsTenantB_ReturnsNotFound()
    {
        // Arrange — create a UserRole in Tenant A
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var roleRepo = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.Application, tenantAId, false));

        var userRoleRepoA = CreateUserRoleRepository(tenantAId);
        var createResult = await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, tenantAId));
        var userRoleId = createResult.Data!.Id;

        // Act — try to delete from Tenant B's context
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        var result = await userRoleRepoB.DeleteAsync(userRoleId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}

