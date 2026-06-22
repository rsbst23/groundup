using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying that Role data NEVER leaks across tenants.
/// Each test uses a real Postgres database via Testcontainers.
/// </summary>
[Collection("AuthPostgres")]
public sealed class RoleTenantIsolationTests : AuthIntegrationTestBase
{
    public RoleTenantIsolationTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateRole_AsTenantA_GetAll_AsTenantA_ReturnsRole()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);

        var roleDto = new RoleDto(Guid.Empty, "Admin Role", "Admin desc", RoleType.Application, tenantAId, false);
        var created = await repoA.AddAsync(roleDto);

        // Act
        var result = await repoA.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle(r => r.Id == created.Data!.Id);
    }

    [Fact]
    public async Task CreateRole_AsTenantA_GetAll_AsTenantB_ReturnsEmpty()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        await repoA.AddAsync(new RoleDto(Guid.Empty, "Admin Role", null, RoleType.Application, tenantAId, false));

        // Act
        var result = await repoB.GetAllAsync(new FilterParams { PageSize = 50 });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRole_AsTenantA_GetById_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var created = await repoA.AddAsync(new RoleDto(Guid.Empty, "Admin Role", null, RoleType.Application, tenantAId, false));

        // Act
        var result = await repoB.GetByIdAsync(created.Data!.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateRole_AsTenantA_Update_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var created = await repoA.AddAsync(new RoleDto(Guid.Empty, "Admin Role", null, RoleType.Application, tenantAId, false));
        var updatedDto = new RoleDto(created.Data!.Id, "Hacked", "Hacked", RoleType.Application, tenantBId, false);

        // Act
        var result = await repoB.UpdateAsync(created.Data!.Id, updatedDto);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CreateRole_AsTenantA_Delete_AsTenantB_ReturnsNotFound()
    {
        // Arrange
        var tenantAId = await SeedTenantAsync("Tenant A", $"tenant-a-{Guid.NewGuid():N}");
        var tenantBId = await SeedTenantAsync("Tenant B", $"tenant-b-{Guid.NewGuid():N}");
        var repoA = CreateRoleRepository(tenantAId);
        var repoB = CreateRoleRepository(tenantBId);

        var created = await repoA.AddAsync(new RoleDto(Guid.Empty, "Admin Role", null, RoleType.Application, tenantAId, false));

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
        var repoA = CreateRoleRepository(tenantAId);

        // Pass a completely different TenantId in the DTO — it should be overwritten
        var spoofedTenantId = Guid.NewGuid();
        var roleDto = new RoleDto(Guid.Empty, "Spoofed Role", null, RoleType.Application, spoofedTenantId, false);

        // Act
        var result = await repoA.AddAsync(roleDto);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.TenantId.Should().Be(tenantAId);
        result.Data!.TenantId.Should().NotBe(spoofedTenantId);
    }
}

