using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// GU-68 Requirements 2.4, 2.5 and 5.1 against real Postgres: finding, linking and merging pending
/// users, and listing members.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PendingUserRepositoryTests : AuthIntegrationTestBase
{
    public PendingUserRepositoryTests(AuthPostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetUnlinkedByEmailAsync_MatchesIgnoringCase_AndSkipsLinkedOrMergedUsers()
    {
        var email = $"Case-{Guid.NewGuid():N}@Example.com";
        var pendingId = await SeedPendingUserAsync(email);
        await SeedUserAsync($"ext-{Guid.NewGuid():N}", email.ToLowerInvariant());
        var mergedId = await SeedPendingUserAsync(email.ToUpperInvariant());
        await DbContext.Users.Where(u => u.Id == mergedId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MergedIntoUserId, Guid.NewGuid()));

        var result = await CreateUserRepository().GetUnlinkedByEmailAsync(email.ToUpperInvariant());

        result.Data.Should().ContainSingle().Which.Id.Should().Be(pendingId);
    }

    [Fact]
    public async Task LinkIdentityAsync_LinksTheUserAndItsMemberships_AndReplacesThePlaceholderName()
    {
        var email = $"link-{Guid.NewGuid():N}@example.com";
        var pendingId = await SeedPendingUserAsync(email);
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        await SeedMembershipAsync(pendingId, tenantId, MembershipStatus.Pending);
        var sub = $"kc-{Guid.NewGuid():N}";

        var result = await CreateUserRepository().LinkIdentityAsync(pendingId, sub, "Ana Real");

        result.Success.Should().BeTrue();
        result.Data!.ExternalUserId.Should().Be(sub);
        result.Data.DisplayName.Should().Be("Ana Real");
        await using var verify = Fixture.CreateContext();
        (await verify.UserTenants.SingleAsync(ut => ut.UserId == pendingId)).ExternalUserId.Should().Be(sub);
    }

    [Fact]
    public async Task LinkIdentityAsync_AlreadyLinked_Conflict()
    {
        var pendingId = await SeedPendingUserAsync($"twice-{Guid.NewGuid():N}@example.com");
        var repository = CreateUserRepository();
        (await repository.LinkIdentityAsync(pendingId, $"kc-a-{Guid.NewGuid():N}", null)).Success.Should().BeTrue();

        var second = await repository.LinkIdentityAsync(pendingId, $"kc-b-{Guid.NewGuid():N}", null);

        second.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task LinkIdentityAsync_IdentityBelongsToAnotherUser_Conflict()
    {
        var sub = $"kc-{Guid.NewGuid():N}";
        await SeedUserAsync(sub, $"owner-{Guid.NewGuid():N}@example.com");
        var pendingId = await SeedPendingUserAsync($"p-{Guid.NewGuid():N}@example.com");

        var result = await CreateUserRepository().LinkIdentityAsync(pendingId, sub, null);

        result.StatusCode.Should().Be(409);
        await using var verify = Fixture.CreateContext();
        (await verify.Users.SingleAsync(u => u.Id == pendingId)).ExternalUserId.Should().BeNull();
    }

    [Fact]
    public async Task MergePendingUserAsync_CoversEachMembershipCase()
    {
        var tenantNew = await SeedTenantAsync("New", $"new-{Guid.NewGuid():N}");
        var tenantActive = await SeedTenantAsync("Active", $"active-{Guid.NewGuid():N}");
        var tenantFormer = await SeedTenantAsync("Former", $"former-{Guid.NewGuid():N}");
        var roleNew = await SeedRoleAsync(tenantNew, "Editor");
        var roleActive = await SeedRoleAsync(tenantActive, "Editor");
        var roleFormer = await SeedRoleAsync(tenantFormer, "Editor");

        var pendingId = await SeedPendingUserAsync($"merge-{Guid.NewGuid():N}@example.com");
        foreach (var (tenant, role) in new[] { (tenantNew, roleNew), (tenantActive, roleActive), (tenantFormer, roleFormer) })
        {
            await SeedMembershipAsync(pendingId, tenant, MembershipStatus.Pending);
            await CreateUserRoleRepository(tenant).AddForTenantAsync(
                new UserRoleDto(Guid.Empty, pendingId, role, tenant, Source: RoleAssignmentSource.Invitation));
        }

        var userId = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"u-{Guid.NewGuid():N}@example.com");
        await SeedMembershipAsync(userId, tenantActive, MembershipStatus.Active);
        await SeedMembershipAsync(userId, tenantFormer, MembershipStatus.Inactive);

        var result = await CreateUserRepository().MergePendingUserAsync(pendingId, userId);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEquivalentTo(new[] { tenantNew, tenantFormer });

        await using var verify = Fixture.CreateContext();
        var memberships = await verify.UserTenants.AsNoTracking().Where(ut => ut.UserId == userId).ToListAsync();
        memberships.Single(m => m.TenantId == tenantNew).Status.Should().Be(MembershipStatus.Pending, "moved to the user");
        memberships.Single(m => m.TenantId == tenantActive).Status.Should().Be(MembershipStatus.Active, "already a member: unchanged");
        memberships.Single(m => m.TenantId == tenantFormer).Status.Should().Be(MembershipStatus.Pending, "takes the pending status");

        var roles = await verify.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.TenantId).ToListAsync();
        roles.Should().BeEquivalentTo(new[] { tenantNew, tenantFormer }, "an active member's roles do not change");

        (await verify.UserTenants.CountAsync(ut => ut.UserId == pendingId)).Should().Be(0);
        (await verify.UserRoles.CountAsync(ur => ur.UserId == pendingId)).Should().Be(0);
        var pending = await verify.Users.AsNoTracking().SingleAsync(u => u.Id == pendingId);
        pending.MergedIntoUserId.Should().Be(userId, "the pending user is kept and marked as merged");
        pending.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task MergePendingUserAsync_UserAlreadyLinked_Conflict()
    {
        var linkedId = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"l-{Guid.NewGuid():N}@example.com");
        var userId = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"u-{Guid.NewGuid():N}@example.com");

        (await CreateUserRepository().MergePendingUserAsync(linkedId, userId)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task GetMembersAsync_ListsActiveAndPendingWithRoleSources_Paged()
    {
        var tenantId = await SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");
        var editor = await SeedRoleAsync(tenantId, "Editor");
        var a = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"a-{Guid.NewGuid():N}@example.com");
        var b = await SeedPendingUserAsync($"b-{Guid.NewGuid():N}@example.com");
        var c = await SeedUserAsync($"kc-{Guid.NewGuid():N}", $"c-{Guid.NewGuid():N}@example.com");
        await SeedMembershipAsync(a, tenantId);
        await SeedMembershipAsync(b, tenantId, MembershipStatus.Pending);
        await SeedMembershipAsync(c, tenantId, MembershipStatus.Inactive);
        var userRoles = CreateUserRoleRepository(tenantId);
        await userRoles.AddForTenantAsync(new UserRoleDto(Guid.Empty, a, editor, tenantId, Source: RoleAssignmentSource.Manual));
        await userRoles.AddForTenantAsync(new UserRoleDto(Guid.Empty, a, editor, tenantId, Source: RoleAssignmentSource.Invitation));

        var repository = CreateUserTenantRepository(tenantId);
        var page1 = await repository.GetMembersAsync(tenantId, new PaginationParams { PageNumber = 1, PageSize = 1 });
        var page2 = await repository.GetMembersAsync(tenantId, new PaginationParams { PageNumber = 2, PageSize = 1 });

        page1.Data!.TotalRecords.Should().Be(2, "the inactive membership is not listed");
        var first = page1.Data.Items.Should().ContainSingle().Subject;
        first.UserId.Should().Be(a, "ordered by email");
        first.Roles.Should().ContainSingle().Which.Sources.Should().Equal(RoleAssignmentSource.Manual, RoleAssignmentSource.Invitation);
        page2.Data!.Items.Should().ContainSingle().Which.Status.Should().Be(MembershipStatus.Pending);
    }

    private async Task<Guid> SeedRoleAsync(Guid tenantId, string name)
    {
        var role = await CreateRoleRepository(tenantId).AddAsync(new RoleDto(Guid.Empty, name, null, RoleType.Application, tenantId, false));
        return role.Data!.Id;
    }
}
