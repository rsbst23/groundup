using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying TenantRepository visibility rules.
/// Tenants can only see themselves and their direct children.
/// Grandchildren, siblings, and unrelated tenants are invisible.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
public sealed class TenantVisibilityTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task GetAll_AsTenantA_ReturnsSelf()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().Contain(t => t.Id == tenantAId);
    }

    [Fact]
    public async Task GetAll_AsTenantA_WithChildB_ReturnsSelfAndChild()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childBId = await SeedTenantAsync("Child B", $"child-b-{Guid.NewGuid():N}", tenantAId);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        var ids = result.Data!.Items.Select(t => t.Id).ToList();
        ids.Should().Contain(tenantAId);
        ids.Should().Contain(childBId);
    }

    [Fact]
    public async Task GetAll_AsTenantA_DoesNotReturnUnrelatedTenantC()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Select(t => t.Id).Should().NotContain(tenantCId);
    }

    [Fact]
    public async Task GetAll_AsTenantA_DoesNotReturnGrandchild()
    {
        // Arrange — A has child B, B has child D (grandchild of A)
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childBId = await SeedTenantAsync("Child B", $"child-b-{Guid.NewGuid():N}", tenantAId);
        var grandchildDId = await SeedTenantAsync("Grandchild D", $"grandchild-d-{Guid.NewGuid():N}", childBId);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        var ids = result.Data!.Items.Select(t => t.Id).ToList();
        ids.Should().Contain(tenantAId);
        ids.Should().Contain(childBId);
        ids.Should().NotContain(grandchildDId);
    }

    [Fact]
    public async Task GetById_ForUnrelatedTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetByIdAsync(tenantCId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetBySlug_ForUnrelatedTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var unrelatedSlug = $"unrelated-{Guid.NewGuid():N}";
        await SeedTenantAsync("Unrelated", unrelatedSlug);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetBySlugAsync(unrelatedSlug);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UpdateAsync_ForUnrelatedTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var updatedDto = new TenantDto(
            tenantCId, "Hacked", $"hacked-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, null, null, null, true);

        // Act
        var result = await repo.UpdateAsync(tenantCId, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_ForUnrelatedTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.DeleteAsync(tenantCId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AddAsync_WithParentTenantId_NotCurrentTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Try to create a child under Tenant C while operating as Tenant A
        var newChildDto = new TenantDto(
            Guid.Empty, "Sneaky Child", $"sneaky-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, tenantCId, null, null, true);

        // Act
        var result = await repo.AddAsync(newChildDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AddAsync_WithParentTenantId_IsCurrentTenant_Succeeds()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var newChildDto = new TenantDto(
            Guid.Empty, "Legit Child", $"legit-child-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, tenantAId, null, null, true);

        // Act
        var result = await repo.AddAsync(newChildDto);

        // Assert
        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.ParentTenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task GetChildTenantsAsync_ForUnrelatedParent_ReturnsEmpty()
    {
        // Arrange — Tenant A asks for children of Tenant C (unrelated)
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantCId = await SeedTenantAsync("Tenant C", $"tenant-c-{Guid.NewGuid():N}");
        // Give Tenant C a child so there IS data — but Tenant A shouldn't see it
        await SeedTenantAsync("Child of C", $"child-c-{Guid.NewGuid():N}", tenantCId);

        var repo = CreateTenantRepository(tenantAId);

        // Act — Tenant A queries for children of Tenant C
        var result = await repo.GetChildTenantsAsync(tenantCId, new FilterParams { PageSize = 50 });

        // Assert — visibility filter excludes Tenant C's children
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }
}
