using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that GetSystemRolesForUserAsync returns only system roles
/// regardless of tenant context, and that RoleName is populated in the result.
/// </summary>
[Collection("AuthPostgres")]
public sealed class UserRoleSystemRolesTests : AuthIntegrationTestBase
{
    public UserRoleSystemRolesTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetSystemRolesForUserAsync_ReturnsOnlySystemRoles()
    {
        // Arrange — user has both system and application roles
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "sysroles@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        // System role
        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "SystemAdmin", null, RoleType.System, tenantId, false));
        sysRole.Success.Should().BeTrue();

        // Application role
        var appRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "AppEditor", null, RoleType.Application, tenantId, false));
        appRole.Success.Should().BeTrue();

        // Assign both to user
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, appRole.Data!.Id, tenantId));

        // Act — query system roles
        var result = await userRoleRepo.GetSystemRolesForUserAsync(userId);

        // Assert — only the system role is returned
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].RoleId.Should().Be(sysRole.Data!.Id);
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_RoleNameIsPopulated()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "rolename@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var sysRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "PlatformOwner", null, RoleType.System, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantId));

        // Act
        var result = await userRoleRepo.GetSystemRolesForUserAsync(userId);

        // Assert — RoleName is populated
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].RoleName.Should().Be("PlatformOwner");
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_BypassesTenantContext()
    {
        // Arrange — system role assigned in tenant A, queried from tenant B context
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "bypass@test.com");

        var roleRepoA = CreateRoleRepository(tenantAId);
        var userRoleRepoA = CreateUserRoleRepository(tenantAId);

        var sysRole = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "GlobalAdmin", null, RoleType.System, tenantAId, false));
        await userRoleRepoA.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole.Data!.Id, tenantAId));

        // Act — query from tenant B context
        var userRoleRepoB = CreateUserRoleRepository(tenantBId);
        var result = await userRoleRepoB.GetSystemRolesForUserAsync(userId);

        // Assert — system role is still returned regardless of tenant context
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].RoleName.Should().Be("GlobalAdmin");
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_MultipleSystemRoles_ReturnsAll()
    {
        // Arrange
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "multi-sys@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var sysRole1 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Admin", null, RoleType.System, tenantId, false));
        var sysRole2 = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Operator", null, RoleType.System, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole1.Data!.Id, tenantId));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, sysRole2.Data!.Id, tenantId));

        // Act
        var result = await userRoleRepo.GetSystemRolesForUserAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(r => r.RoleName).Should().Contain("Admin");
        result.Data!.Select(r => r.RoleName).Should().Contain("Operator");
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_NoSystemRoles_ReturnsEmptyList()
    {
        // Arrange — user has only application roles
        var tenantId = await SeedTenantAsync("Test Tenant", $"tenant-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", "nosys@test.com");

        var roleRepo = CreateRoleRepository(tenantId);
        var userRoleRepo = CreateUserRoleRepository(tenantId);

        var appRole = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "AppUser", null, RoleType.Application, tenantId, false));
        await userRoleRepo.AddAsync(new UserRoleDto(Guid.Empty, userId, appRole.Data!.Id, tenantId));

        // Act
        var result = await userRoleRepo.GetSystemRolesForUserAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }
}

