using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying tenant isolation for tenant-scoped repositories.
/// Property 6: Tenant-scoped repositories enforce tenant isolation.
/// Validates: Requirements 11.5, 11.6
/// </summary>
public sealed class TenantIsolationTests : AuthIntegrationTestBase
{
    #region RoleRepository Isolation

    [Fact]
    public async Task RoleRepository_CreateAsTenantA_QueryAsTenantA_ReturnsRole()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin", RoleType.Application, tenantAId, false);
        var created = await repoA.AddAsync(roleDto);

        // Act
        var result = await repoA.GetByIdAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Admin Role");
    }

    [Fact]
    public async Task RoleRepository_CreateAsTenantA_QueryAsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin", RoleType.Application, tenantAId, false);
        await repoA.AddAsync(roleDto);

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RoleRepository_CreateAsTenantA_GetByIdAsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin", RoleType.Application, tenantAId, false);
        var created = await repoA.AddAsync(roleDto);

        // Act
        var result = await repoB.GetByIdAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RoleRepository_CreateAsTenantA_UpdateAsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin", RoleType.Application, tenantAId, false);
        var created = await repoA.AddAsync(roleDto);

        var updatedDto = new RoleDto(created.Data!.Id, "Hacked", "Hacked", RoleType.Application, tenantBId, false);

        // Act
        var result = await repoB.UpdateAsync(created.Data!.Id, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RoleRepository_CreateAsTenantA_DeleteAsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin", RoleType.Application, tenantAId, false);
        var created = await repoA.AddAsync(roleDto);

        // Act
        var result = await repoB.DeleteAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RoleRepository_CreateForBothTenants_GetAllAsTenantA_ReturnsOnlyTenantARoles()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        await repoA.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));
        await repoB.AddAsync(new RoleDto(Guid.Empty, "Role B", null, RoleType.Application, tenantBId, false));

        // Act
        var result = await repoA.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data!.Items.First().Name.Should().Be("Role A");
    }

    [Fact]
    public async Task RoleRepository_AddAsync_AutomaticallySetsTenantId()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);

        // Pass a different TenantId in the DTO — it should be overwritten
        var wrongTenantId = Guid.NewGuid();
        var roleDto = new RoleDto(Guid.Empty, "Test Role", null, RoleType.Application, wrongTenantId, false);

        // Act
        var result = await repoA.AddAsync(roleDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    #endregion

    #region PolicyRepository Isolation

    [Fact]
    public async Task PolicyRepository_CreateAsTenantA_QueryAsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        await repoA.AddAsync(new PolicyDto(Guid.Empty, "Policy A", "Desc", tenantAId));

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task PolicyRepository_CreateAsTenantA_GetByIdAsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        var created = await repoA.AddAsync(new PolicyDto(Guid.Empty, "Policy A", "Desc", tenantAId));

        // Act
        var result = await repoB.GetByIdAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task PolicyRepository_AddAsync_AutomaticallySetsTenantId()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);

        var wrongTenantId = Guid.NewGuid();
        var policyDto = new PolicyDto(Guid.Empty, "Test Policy", null, wrongTenantId);

        // Act
        var result = await repoA.AddAsync(policyDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    #endregion

    #region UserTenantRepository Isolation

    [Fact]
    public async Task UserTenantRepository_CreateAsTenantA_QueryAsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);

        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task UserTenantRepository_AddAsync_AutomaticallySetsTenantId()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");
        var repoA = CreateUserTenantRepository(tenantAId);

        var wrongTenantId = Guid.NewGuid();
        var dto = new UserTenantDto(Guid.Empty, userId, wrongTenantId, $"ext-{Guid.NewGuid():N}", true);

        // Act
        var result = await repoA.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    #endregion

    #region UserRoleRepository Isolation

    [Fact]
    public async Task UserRoleRepository_CreateAsTenantA_QueryAsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        // Create a role in tenant A first
        var roleRepoA = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepoA.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));

        var repoA = CreateUserRoleRepository(tenantAId);
        var repoB = CreateUserRoleRepository(tenantBId);

        await repoA.AddAsync(new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, tenantAId));

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task UserRoleRepository_AddAsync_AutomaticallySetsTenantId()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var roleRepo = CreateRoleRepository(tenantAId);
        var roleResult = await roleRepo.AddAsync(new RoleDto(Guid.Empty, "Role A", null, RoleType.Application, tenantAId, false));

        var repoA = CreateUserRoleRepository(tenantAId);
        var wrongTenantId = Guid.NewGuid();
        var dto = new UserRoleDto(Guid.Empty, userId, roleResult.Data!.Id, wrongTenantId);

        // Act
        var result = await repoA.AddAsync(dto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    #endregion
}
