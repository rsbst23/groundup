using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// GU-95 against real Postgres: what uses a role (checked before a delete), and the tenant's default role.
/// </summary>
[Collection("AuthPostgres")]
public sealed class RoleUsageRepositoryTests : AuthIntegrationTestBase
{
    public RoleUsageRepositoryTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetUsageAsync_UnusedRole_AllZero()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var roleId = await SeedRoleAsync(tenantId, "Viewer");

        var usage = await CreateRoleRepository(tenantId).GetUsageAsync(roleId);

        usage.Data.Should().Be(new RoleUsageDto(0, 0, 0, false));
        usage.Data!.IsUnused.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsageAsync_CountsHoldersPendingInvitationsActiveLinksAndTheDefault()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var roleId = await SeedRoleAsync(tenantId, "Member");
        var creator = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"creator-{Guid.NewGuid():N}@example.com");
        var holder = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"holder-{Guid.NewGuid():N}@example.com");
        var pendingHolder = await SeedPendingUserAsync($"pending-{Guid.NewGuid():N}@example.com");
        await SeedMembershipAsync(holder, tenantId);
        await SeedMembershipAsync(pendingHolder, tenantId, MembershipStatus.Pending);
        await GrantAsync(holder, roleId, tenantId);
        await GrantAsync(pendingHolder, roleId, tenantId);
        await SeedInvitationAsync(tenantId, roleId, pendingHolder, creator, InvitationStatus.Pending);
        await SeedInvitationAsync(tenantId, roleId, pendingHolder, creator, InvitationStatus.Expired);
        await SeedInvitationAsync(tenantId, roleId, pendingHolder, creator, InvitationStatus.Revoked);
        await SeedInvitationAsync(tenantId, roleId, pendingHolder, creator, InvitationStatus.Accepted);
        await SeedJoinLinkAsync(tenantId, roleId, creator, JoinLinkStatus.Active);
        await SeedJoinLinkAsync(tenantId, roleId, creator, JoinLinkStatus.Revoked);
        (await CreateTenantRepository(tenantId).SetDefaultRoleIdAsync(tenantId, roleId)).Success.Should().BeTrue();

        var usage = await CreateRoleRepository(tenantId).GetUsageAsync(roleId);

        usage.Data.Should().Be(new RoleUsageDto(Holders: 2, PendingInvitations: 2, ActiveJoinLinks: 1, IsTenantDefault: true));
        usage.Data!.IsUnused.Should().BeFalse();
    }

    [Fact]
    public async Task GetUsageAsync_RoleOfAnotherTenant_NotFound()
    {
        var tenantA = await SeedTenantAsync("A", $"a-{Guid.NewGuid():N}");
        var tenantB = await SeedTenantAsync("B", $"b-{Guid.NewGuid():N}");
        var roleOfB = await SeedRoleAsync(tenantB, "Member");

        var usage = await CreateRoleRepository(tenantA).GetUsageAsync(roleOfB);

        usage.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DefaultRoleId_SetReadAndClear()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var roleId = await SeedRoleAsync(tenantId, "Member");
        var tenants = CreateTenantRepository(tenantId);

        (await tenants.GetDefaultRoleIdAsync(tenantId)).Data.Should().BeNull();
        await tenants.SetDefaultRoleIdAsync(tenantId, roleId);
        (await tenants.GetDefaultRoleIdAsync(tenantId)).Data.Should().Be(roleId);
        await tenants.SetDefaultRoleIdAsync(tenantId, null);
        (await tenants.GetDefaultRoleIdAsync(tenantId)).Data.Should().BeNull();
    }

    [Fact]
    public async Task DefaultRoleId_UnknownTenant_NotFound()
    {
        var tenants = CreateTenantRepository(Guid.NewGuid());

        (await tenants.GetDefaultRoleIdAsync(Guid.NewGuid())).StatusCode.Should().Be(404);
        (await tenants.SetDefaultRoleIdAsync(Guid.NewGuid(), null)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task TenantUpdate_KeepsTheDefaultRole()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var roleId = await SeedRoleAsync(tenantId, "Member");
        var tenants = CreateTenantRepository(tenantId);
        await tenants.SetDefaultRoleIdAsync(tenantId, roleId);
        var tenant = (await tenants.GetByIdAsync(tenantId)).Data!;

        (await tenants.UpdateAsync(tenantId, tenant with { Name = "Renamed" })).Success.Should().BeTrue();

        (await CreateTenantRepository(tenantId).GetDefaultRoleIdAsync(tenantId)).Data.Should().Be(roleId);
    }

    private async Task<Guid> SeedRoleAsync(Guid tenantId, string name)
    {
        var added = await CreateRoleRepository(tenantId)
            .AddAsync(new RoleDto(Guid.Empty, name, null, RoleType.Application, tenantId, false));
        added.Success.Should().BeTrue(added.Message);
        return added.Data!.Id;
    }

    private async Task GrantAsync(Guid userId, Guid roleId, Guid tenantId)
    {
        DbContext.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId, TenantId = tenantId, Source = RoleAssignmentSource.Manual });
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedInvitationAsync(Guid tenantId, Guid roleId, Guid userId, Guid creator, InvitationStatus status)
    {
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        DbContext.Invitations.Add(new Invitation
        {
            TenantId = tenantId,
            Email = email,
            NormalizedEmail = email,
            RoleId = roleId,
            UserId = userId,
            CreatedByUserId = creator,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(status == InvitationStatus.Expired ? -1 : 7),
            Status = status
        });
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedJoinLinkAsync(Guid tenantId, Guid roleId, Guid creator, JoinLinkStatus status)
    {
        DbContext.JoinLinks.Add(new JoinLink
        {
            TenantId = tenantId,
            RoleId = roleId,
            CreatedByUserId = creator,
            TokenHash = Guid.NewGuid().ToString("N"),
            Status = status
        });
        await DbContext.SaveChangesAsync();
    }
}
