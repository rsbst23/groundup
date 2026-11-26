using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// GU-68 Requirement 2.3 and 4.2: role lookups that feed permissions, tokens and the last-admin
/// guard return only roles held through an Active membership, and each role once even when it is
/// held from several sources.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PendingMembershipRoleTests : AuthIntegrationTestBase
{
    public PendingMembershipRoleTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData(MembershipStatus.Pending)]
    [InlineData(MembershipStatus.Inactive)]
    public async Task GetByUserIdForTenantAsync_MembershipNotActive_ReturnsNoRoles(MembershipStatus status)
    {
        var (tenantId, userId, roleId) = await SeedMemberWithRoleAsync(status, "Editor");

        var result = await CreateUserRoleRepository(tenantId).GetByUserIdForTenantAsync(userId, tenantId);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty("roles of a {0} membership have no effect", status);
        (await CountAssignmentsAsync(userId, roleId)).Should().Be(1, "the role row itself is kept");
    }

    [Fact]
    public async Task GetByUserIdAsync_PendingMembership_ReturnsNoRoles()
    {
        var (tenantId, userId, _) = await SeedMemberWithRoleAsync(MembershipStatus.Pending, "Editor");

        var result = await CreateUserRoleRepository(tenantId).GetByUserIdAsync(userId);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByUserIdAsync_ActiveMembership_ReturnsRole()
    {
        var (tenantId, userId, roleId) = await SeedMemberWithRoleAsync(MembershipStatus.Active, "Editor");

        var result = await CreateUserRoleRepository(tenantId).GetByUserIdAsync(userId);

        result.Data.Should().ContainSingle().Which.RoleId.Should().Be(roleId);
    }

    [Fact]
    public async Task GetByUserIdForTenantAsync_SameRoleFromTwoSources_ReturnsRoleOnce()
    {
        var (tenantId, userId, roleId) = await SeedMemberWithRoleAsync(MembershipStatus.Active, "Editor");
        await CreateUserRoleRepository(tenantId).AddAsync(
            new UserRoleDto(Guid.Empty, userId, roleId, tenantId, Source: RoleAssignmentSource.Invitation));

        var result = await CreateUserRoleRepository(tenantId).GetByUserIdForTenantAsync(userId, tenantId);

        (await CountAssignmentsAsync(userId, roleId)).Should().Be(2);
        result.Data.Should().ContainSingle().Which.RoleName.Should().Be("Editor");
    }

    [Fact]
    public async Task GetSystemRolesForUserAsync_PendingMembership_ReturnsNoRoles()
    {
        var tenantId = await SeedTenantAsync("Sys", $"sys-{Guid.NewGuid():N}");
        var userId = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"sys-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(userId, tenantId, MembershipStatus.Pending);
        var role = await CreateRoleRepository(tenantId).AddAsync(
            new RoleDto(Guid.Empty, "Operator", null, RoleType.System, tenantId, false));
        await CreateUserRoleRepository(tenantId).AddAsync(new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));

        var result = await CreateUserRoleRepository(tenantId).GetSystemRolesForUserAsync(userId);

        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTenantAdminHoldersAsync_ExcludesPendingAdmins_AndCountsEachUserOnce()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var adminRole = await CreateRoleRepository(tenantId).AddAsync(
            new RoleDto(Guid.Empty, AuthRoleNames.TenantAdmin, null, RoleType.Application, tenantId, true));
        var userRoles = CreateUserRoleRepository(tenantId);

        var activeAdmin = await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"a-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(activeAdmin, tenantId);
        await userRoles.AddAsync(new UserRoleDto(Guid.Empty, activeAdmin, adminRole.Data!.Id, tenantId, Source: RoleAssignmentSource.Provisioning));
        await userRoles.AddAsync(new UserRoleDto(Guid.Empty, activeAdmin, adminRole.Data!.Id, tenantId, Source: RoleAssignmentSource.Manual));

        var pendingAdmin = await SeedPendingUserAsync($"p-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(pendingAdmin, tenantId, MembershipStatus.Pending);
        await userRoles.AddAsync(new UserRoleDto(Guid.Empty, pendingAdmin, adminRole.Data!.Id, tenantId, Source: RoleAssignmentSource.Invitation));

        var result = await userRoles.GetTenantAdminHoldersAsync(tenantId);

        result.Data.Should().ContainSingle().Which.UserId.Should().Be(activeAdmin);
    }

    [Fact]
    public async Task GetAllMembershipsForUserAsync_PendingMembership_IsNotActive()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var userId = await SeedPendingUserAsync($"p-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(userId, tenantId, MembershipStatus.Pending);

        var result = await CreateUserTenantRepository(tenantId).GetAllMembershipsForUserAsync(userId);

        var membership = result.Data.Should().ContainSingle().Subject;
        membership.Status.Should().Be(MembershipStatus.Pending);
        membership.ExternalUserId.Should().BeNull();
        membership.IsActive.Should().BeFalse("picker, IsActiveMemberAsync and the sign-in flows read IsActive");
    }

    private async Task<(Guid TenantId, Guid UserId, Guid RoleId)> SeedMemberWithRoleAsync(MembershipStatus status, string roleName)
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var userId = status == MembershipStatus.Pending
            ? await SeedPendingUserAsync($"p-{Guid.NewGuid():N}@test.com")
            : await SeedUserAsync($"ext-{Guid.NewGuid():N}", $"u-{Guid.NewGuid():N}@test.com");
        await SeedMembershipAsync(userId, tenantId, status);

        var role = await CreateRoleRepository(tenantId).AddAsync(
            new RoleDto(Guid.Empty, roleName, null, RoleType.Application, tenantId, false));
        var assignment = await CreateUserRoleRepository(tenantId).AddAsync(
            new UserRoleDto(Guid.Empty, userId, role.Data!.Id, tenantId));
        assignment.Success.Should().BeTrue();

        return (tenantId, userId, role.Data.Id);
    }

    private Task<int> CountAssignmentsAsync(Guid userId, Guid roleId)
    {
        using var context = Fixture.CreateContext();
        return Task.FromResult(context.UserRoles.Count(ur => ur.UserId == userId && ur.RoleId == roleId));
    }
}
