using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying TenantRepository visibility rules.
/// Property 2: TenantRepository visibility restricts to self and direct children.
/// Validates: Requirements 10.2, 10.3, 10.4, 10.5
/// </summary>
[Collection("AuthPostgres")]
public sealed class TenantRepositoryVisibilityTests : AuthIntegrationTestBase
{
    public TenantRepositoryVisibilityTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetAllAsync_AsTenantA_ReturnsOnlySelfAndDirectChildren()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childOfAId = await SeedTenantAsync("Child of A", $"child-a-{Guid.NewGuid():N}", tenantAId);
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var childOfBId = await SeedTenantAsync("Child of B", $"child-b-{Guid.NewGuid():N}", tenantBId);

        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        var ids = result.Data!.Items.Select(t => t.Id).ToList();
        ids.Should().Contain(tenantAId);
        ids.Should().Contain(childOfAId);
        ids.Should().NotContain(tenantBId);
        ids.Should().NotContain(childOfBId);
    }

    [Fact]
    public async Task GetByIdAsync_ForOwnTenant_ReturnsTenant()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetByIdAsync(tenantAId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(tenantAId);
    }

    [Fact]
    public async Task GetByIdAsync_ForChildTenant_ReturnsChild()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childOfAId = await SeedTenantAsync("Child of A", $"child-a-{Guid.NewGuid():N}", tenantAId);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetByIdAsync(childOfAId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(childOfAId);
    }

    [Fact]
    public async Task GetByIdAsync_ForSiblingTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetByIdAsync(tenantBId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByIdAsync_ForGrandchildTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childOfAId = await SeedTenantAsync("Child of A", $"child-a-{Guid.NewGuid():N}", tenantAId);
        var grandchildId = await SeedTenantAsync("Grandchild", $"grandchild-{Guid.NewGuid():N}", childOfAId);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetByIdAsync(grandchildId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetBySlugAsync_ForOwnTenantSlug_ReturnsIt()
    {
        // Arrange
        var slug = $"my-slug-{Guid.NewGuid():N}";
        var tenantAId = await SeedTenantAsync("Tenant A", slug);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetBySlugAsync(slug);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Slug.Should().Be(slug);
    }

    [Fact]
    public async Task GetBySlugAsync_ForAnotherTenantsSlug_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBSlug = $"tenant-b-{Guid.NewGuid():N}";
        await SeedTenantAsync("Tenant B", tenantBSlug);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetBySlugAsync(tenantBSlug);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetChildTenantsAsync_ReturnsOnlyDirectChildrenFilteredByVisibility()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var child1Id = await SeedTenantAsync("Child 1", $"child-1-{Guid.NewGuid():N}", tenantAId);
        var child2Id = await SeedTenantAsync("Child 2", $"child-2-{Guid.NewGuid():N}", tenantAId);
        var grandchildId = await SeedTenantAsync("Grandchild", $"grandchild-{Guid.NewGuid():N}", child1Id);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.GetChildTenantsAsync(tenantAId, new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        var ids = result.Data!.Items.Select(t => t.Id).ToList();
        ids.Should().Contain(child1Id);
        ids.Should().Contain(child2Id);
        ids.Should().NotContain(grandchildId);
    }

    [Fact]
    public async Task UpdateAsync_ForOwnTenant_Succeeds()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var updatedDto = new TenantDto(
            tenantAId, "Updated Name", $"tenant-a-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, null, null, true);

        // Act
        var result = await repo.UpdateAsync(tenantAId, updatedDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateAsync_ForChildTenant_Succeeds()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childId = await SeedTenantAsync("Child", $"child-{Guid.NewGuid():N}", tenantAId);
        var repo = CreateTenantRepository(tenantAId);

        var updatedDto = new TenantDto(
            childId, "Updated Child", $"child-updated-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, tenantAId, null, true);

        // Act
        var result = await repo.UpdateAsync(childId, updatedDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Name.Should().Be("Updated Child");
    }

    [Fact]
    public async Task UpdateAsync_ForSiblingTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var updatedDto = new TenantDto(
            tenantBId, "Hacked", $"hacked-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, null, null, true);

        // Act
        var result = await repo.UpdateAsync(tenantBId, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_ForChildTenant_SoftDeletesIt()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var childId = await SeedTenantAsync("Child", $"child-{Guid.NewGuid():N}", tenantAId);
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.DeleteAsync(childId);

        // Assert
        result.Success.Should().BeTrue();

        // Verify soft-deleted (not visible via normal query)
        var getResult = await repo.GetByIdAsync(childId);
        getResult.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ForSiblingTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        // Act
        var result = await repo.DeleteAsync(tenantBId);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AddAsync_WithParentTenantIdEqualToCurrentTenant_Succeeds()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var newChildDto = new TenantDto(
            Guid.Empty, "New Child", $"new-child-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, tenantAId, null, true);

        // Act
        var result = await repo.AddAsync(newChildDto);

        // Assert
        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.ParentTenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task AddAsync_WithParentTenantIdEqualToDifferentTenant_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repo = CreateTenantRepository(tenantAId);

        var newChildDto = new TenantDto(
            Guid.Empty, "Sneaky Child", $"sneaky-{Guid.NewGuid():N}",
            TenantType.Standard, OnboardingMode.InviteOnly, tenantBId, null, true);

        // Act
        var result = await repo.AddAsync(newChildDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}

