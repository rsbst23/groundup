using FluentAssertions;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that UserTenant membership data respects tenant isolation,
/// with the exception of the system bypass method GetAllMembershipsForUserAsync.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
public sealed class UserTenantIsolationTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task CreateUserTenant_AsTenantA_GetByUserId_AsTenantA_ReturnsIt()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));

        // Act
        var result = await repoA.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.UserId.Should().Be(userId);
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task CreateUserTenant_AsTenantA_GetByUserId_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));

        // Act — query from Tenant B's context
        var repoB = CreateUserTenantRepository(tenantBId);
        var result = await repoB.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAllMembershipsForUser_ReturnsBothTenants_RegardlessOfCurrentTenant()
    {
        // Arrange — SYSTEM BYPASS test
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);
        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        await repoB.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantBId, $"ext-b-{Guid.NewGuid():N}", true));

        // Act — call from Tenant A's context, should still return both
        var result = await repoA.GetAllMembershipsForUserAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
        result.Data!.Select(m => m.TenantId).Should().Contain(tenantAId);
        result.Data!.Select(m => m.TenantId).Should().Contain(tenantBId);
    }

    [Fact]
    public async Task UpdateUserTenant_AsTenantB_ReturnsNotFound()
    {
        // Arrange — create a UserTenant in Tenant A
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var createResult = await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        var userTenantId = createResult.Data!.Id;

        // Act — try to update from Tenant B's context
        var repoB = CreateUserTenantRepository(tenantBId);
        var updatedDto = new UserTenantDto(userTenantId, userId, tenantAId, $"ext-hacked-{Guid.NewGuid():N}", true);
        var result = await repoB.UpdateAsync(userTenantId, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteUserTenant_AsTenantB_ReturnsNotFound()
    {
        // Arrange — create a UserTenant in Tenant A
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var createResult = await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        var userTenantId = createResult.Data!.Id;

        // Act — try to delete from Tenant B's context
        var repoB = CreateUserTenantRepository(tenantBId);
        var result = await repoB.DeleteAsync(userTenantId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
