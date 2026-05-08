using FluentAssertions;
using GroundUp.Auth.Core.Dtos;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying the system bypass method for UserTenantRepository.
/// Property 7: GetAllMembershipsForUserAsync bypasses tenant filtering.
/// Validates: Requirements 7.3
/// </summary>
public sealed class UserTenantSystemBypassTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task GetAllMembershipsForUserAsync_ReturnsBothMemberships_RegardlessOfTenantContext()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);

        // Create memberships in both tenants
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
    public async Task GetByUserIdAsync_AsTenantA_ReturnsOnlyTenantAMembership()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);

        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        await repoB.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantBId, $"ext-b-{Guid.NewGuid():N}", true));

        // Act
        var result = await repoA.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task GetByUserIdAsync_AsTenantB_ReturnsOnlyTenantBMembership()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);

        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        await repoB.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantBId, $"ext-b-{Guid.NewGuid():N}", true));

        // Act
        var result = await repoB.GetByUserIdAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantBId);
    }

    [Fact]
    public async Task GetAllMembershipsForUserAsync_CountEqualsTotalMemberships()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");

        var repoA = CreateUserTenantRepository(tenantAId);
        var repoB = CreateUserTenantRepository(tenantBId);
        var repoC = CreateUserTenantRepository(tenantCId);

        await repoA.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantAId, $"ext-a-{Guid.NewGuid():N}", true));
        await repoB.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantBId, $"ext-b-{Guid.NewGuid():N}", true));
        await repoC.AddAsync(new UserTenantDto(Guid.Empty, userId, tenantCId, $"ext-c-{Guid.NewGuid():N}", true));

        // Act — call from any tenant context
        var result = await repoA.GetAllMembershipsForUserAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Should().HaveCount(3);
    }
}
