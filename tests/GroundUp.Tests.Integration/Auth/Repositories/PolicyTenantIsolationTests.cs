using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that Policy data NEVER leaks across tenants.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
public sealed class PolicyTenantIsolationTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task CreatePolicy_AsTenantA_GetAll_AsTenantA_ReturnsPolicy()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);

        var policyDto = new PolicyDto(Guid.Empty, "Read Policy", "Allows reading", tenantAId);
        var created = await repoA.AddAsync(policyDto);

        // Act
        var result = await repoA.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle(p => p.Id == created.Data!.Id);
    }

    [Fact]
    public async Task CreatePolicy_AsTenantA_GetAll_AsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        await repoA.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantAId));

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePolicy_AsTenantA_GetById_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        var created = await repoA.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantAId));

        // Act
        var result = await repoB.GetByIdAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreatePolicy_AsTenantA_Update_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        var created = await repoA.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantAId));
        var updatedDto = new PolicyDto(created.Data!.Id, "Hacked Policy", "Hacked", tenantBId);

        // Act
        var result = await repoB.UpdateAsync(created.Data!.Id, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreatePolicy_AsTenantA_Delete_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);
        var repoB = CreatePolicyRepository(tenantBId);

        var created = await repoA.AddAsync(new PolicyDto(Guid.Empty, "Read Policy", null, tenantAId));

        // Act
        var result = await repoB.DeleteAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AddAsync_AutoSetsTenantId_ToCurrentTenant()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreatePolicyRepository(tenantAId);

        // Pass a completely different TenantId in the DTO — it should be overwritten
        var spoofedTenantId = Guid.NewGuid();
        var policyDto = new PolicyDto(Guid.Empty, "Spoofed Policy", null, spoofedTenantId);

        // Act
        var result = await repoA.AddAsync(policyDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
        result.Data!.TenantId.Should().NotBe(spoofedTenantId);
    }
}
