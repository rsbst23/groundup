using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// GU-96 against real Postgres: policy lists and details, replacing a policy's permissions, deleting
/// a policy, a role's permission grants, attaching and detaching policies, and the permission catalogue.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PolicyRepositoryTests : AuthIntegrationTestBase
{
    public PolicyRepositoryTests(AuthPostgresFixture fixture) : base(fixture) { }

    // ── Policies ──────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_OwnPoliciesByNameWithKeysAndRoleCounts()
    {
        var tenantId = await NewTenantAsync();
        var otherTenantId = await NewTenantAsync();
        var (readKey, writeKey) = (Key("read"), Key("write"));
        var read = await SeedPermissionAsync(readKey, "Read", "test");
        var write = await SeedPermissionAsync(writeKey, "Write", "test");
        var editors = await SeedPolicyAsync(tenantId, "Editors");
        var auditors = await SeedPolicyAsync(tenantId, "Auditors");
        await SeedPolicyAsync(otherTenantId, "Foreign");
        await LinkPermissionAsync(editors, write);
        await LinkPermissionAsync(editors, read);
        var role = await SeedRoleAsync(tenantId, "Editor");
        await LinkPolicyAsync(role, editors);
        await LinkPolicyAsync(await SeedRoleAsync(otherTenantId, "Intruder"), editors);

        var page = await CreatePolicyRepository(tenantId).ListAsync(new FilterParams { PageSize = 10 });

        page.Data!.TotalRecords.Should().Be(2);
        page.Data.Items.Select(p => p.Name).Should().Equal("Auditors", "Editors");
        var editorsRow = page.Data.Items.Single(p => p.Id == editors);
        editorsRow.PermissionKeys.Should().Equal(new[] { readKey, writeKey }.Order(StringComparer.Ordinal));
        editorsRow.RoleCount.Should().Be(1, "a link to another tenant's role is not counted");
        page.Data.Items.Single(p => p.Id == auditors).PermissionKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_Pages()
    {
        var tenantId = await NewTenantAsync();
        foreach (var name in new[] { "A", "B", "C" })
        {
            await SeedPolicyAsync(tenantId, name);
        }

        var page = await CreatePolicyRepository(tenantId).ListAsync(new FilterParams { PageNumber = 2, PageSize = 2 });

        page.Data!.Items.Select(p => p.Name).Should().Equal("C");
        page.Data.TotalRecords.Should().Be(3);
    }

    [Fact]
    public async Task GetDetailAsync_PermissionsAndOnlyTheTenantsRoles()
    {
        var tenantId = await NewTenantAsync();
        var otherTenantId = await NewTenantAsync();
        var readKey = Key("read");
        var permission = await SeedPermissionAsync(readKey, "Read", "test");
        var policy = await SeedPolicyAsync(tenantId, "Readers");
        await LinkPermissionAsync(policy, permission);
        var viewer = await SeedRoleAsync(tenantId, "Viewer");
        var auditor = await SeedRoleAsync(tenantId, "Auditor");
        await LinkPolicyAsync(viewer, policy);
        await LinkPolicyAsync(auditor, policy);
        await LinkPolicyAsync(await SeedRoleAsync(otherTenantId, "Intruder"), policy);

        var detail = await CreatePolicyRepository(tenantId).GetDetailAsync(policy);

        detail.Data!.Name.Should().Be("Readers");
        detail.Data.Permissions.Select(p => p.Key).Should().Equal(readKey);
        detail.Data.Roles.Should().Equal(new EntityRefDto(auditor, "Auditor"), new EntityRefDto(viewer, "Viewer"));
    }

    [Fact]
    public async Task GetDetailAsync_PolicyOfAnotherTenant_NotFound()
    {
        var policy = await SeedPolicyAsync(await NewTenantAsync(), "Foreign");

        (await CreatePolicyRepository(await NewTenantAsync()).GetDetailAsync(policy)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ReplacePermissionsAsync_AddsAndRemovesAndReportsBoth()
    {
        var tenantId = await NewTenantAsync();
        var keep = await SeedPermissionAsync(Key("keep"), "Keep", "test");
        var drop = await SeedPermissionAsync(Key("drop"), "Drop", "test");
        var add = await SeedPermissionAsync(Key("add"), "Add", "test");
        var policy = await SeedPolicyAsync(tenantId, "Mixed");
        await LinkPermissionAsync(policy, keep);
        await LinkPermissionAsync(policy, drop);

        var changes = await CreatePolicyRepository(tenantId).ReplacePermissionsAsync(policy, [keep, add]);

        changes.Success.Should().BeTrue(changes.Message);
        changes.Data!.Added.Should().ContainSingle(l => l.PermissionId == add && l.PolicyId == policy && l.Id != Guid.Empty);
        changes.Data.Removed.Should().ContainSingle(l => l.PermissionId == drop);
        (await LinkedPermissionsAsync(policy)).Should().BeEquivalentTo(new[] { keep, add });
    }

    [Fact]
    public async Task ReplacePermissionsAsync_SameSet_NoChanges()
    {
        var tenantId = await NewTenantAsync();
        var keep = await SeedPermissionAsync(Key("keep"), "Keep", "test");
        var policy = await SeedPolicyAsync(tenantId, "Same");
        await LinkPermissionAsync(policy, keep);

        var changes = await CreatePolicyRepository(tenantId).ReplacePermissionsAsync(policy, [keep]);

        changes.Data!.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task ReplacePermissionsAsync_EmptySet_RemovesAll()
    {
        var tenantId = await NewTenantAsync();
        var policy = await SeedPolicyAsync(tenantId, "Emptied");
        await LinkPermissionAsync(policy, await SeedPermissionAsync(Key("one"), "One", "test"));

        var changes = await CreatePolicyRepository(tenantId).ReplacePermissionsAsync(policy, []);

        changes.Data!.Removed.Should().HaveCount(1);
        (await LinkedPermissionsAsync(policy)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplacePermissionsAsync_PolicyOfAnotherTenant_NotFound()
    {
        var policy = await SeedPolicyAsync(await NewTenantAsync(), "Foreign");

        (await CreatePolicyRepository(await NewTenantAsync()).ReplacePermissionsAsync(policy, [])).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteAsync_UsedByARole_ConflictAndKept()
    {
        var tenantId = await NewTenantAsync();
        var policy = await SeedPolicyAsync(tenantId, "Used");
        await LinkPolicyAsync(await SeedRoleAsync(tenantId, "Editor"), policy);
        // A request's context does not track the link, so the database's foreign key decides (as in the API)
        DbContext.ChangeTracker.Clear();

        var deleted = await CreatePolicyRepository(tenantId).DeleteAsync(policy);

        deleted.StatusCode.Should().Be(409);
        (await DbContext.Policies.AsNoTracking().AnyAsync(p => p.Id == policy)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_Unused_DeletedWithItsPermissionLinks()
    {
        var tenantId = await NewTenantAsync();
        var policy = await SeedPolicyAsync(tenantId, "Unused");
        await LinkPermissionAsync(policy, await SeedPermissionAsync(Key("one"), "One", "test"));

        (await CreatePolicyRepository(tenantId).DeleteAsync(policy)).Success.Should().BeTrue();

        (await DbContext.Policies.AsNoTracking().AnyAsync(p => p.Id == policy)).Should().BeFalse();
        (await LinkedPermissionsAsync(policy)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_SameNameInAnotherCase_Conflict()
    {
        var tenantId = await NewTenantAsync();
        await SeedPolicyAsync(tenantId, "Editors");

        var added = await CreatePolicyRepository(tenantId).AddAsync(new PolicyDto(Guid.Empty, "EDITORS", null, tenantId));

        added.StatusCode.Should().Be(409);
    }

    // ── Roles ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetPermissionGrantsAsync_OneRowPerPermissionAndPolicy()
    {
        var tenantId = await NewTenantAsync();
        var (readKey, writeKey) = (Key("read"), Key("write"));
        var read = await SeedPermissionAsync(readKey, "Read", "test");
        var write = await SeedPermissionAsync(writeKey, "Write", "test");
        var readers = await SeedPolicyAsync(tenantId, "Readers");
        var writers = await SeedPolicyAsync(tenantId, "Writers");
        await LinkPermissionAsync(readers, read);
        await LinkPermissionAsync(writers, read);
        await LinkPermissionAsync(writers, write);
        var role = await SeedRoleAsync(tenantId, "Editor");
        await LinkPolicyAsync(role, readers);
        await LinkPolicyAsync(role, writers);

        // Any tenant context: the read is by role id
        var grants = await CreateRoleRepository(Guid.NewGuid()).GetPermissionGrantsAsync(role);

        grants.Data.Should().BeEquivalentTo(new[]
        {
            new RoleGrantDto(readKey, readers, "Readers"),
            new RoleGrantDto(readKey, writers, "Writers"),
            new RoleGrantDto(writeKey, writers, "Writers")
        });
    }

    [Fact]
    public async Task GetPermissionGrantsAsync_RoleWithoutPolicies_Empty()
    {
        var tenantId = await NewTenantAsync();
        var role = await SeedRoleAsync(tenantId, "Plain");

        (await CreateRoleRepository(tenantId).GetPermissionGrantsAsync(role)).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignPolicyAsync_CreatedThenExisting_SameLink()
    {
        var tenantId = await NewTenantAsync();
        var role = await SeedRoleAsync(tenantId, "Editor");
        var policy = await SeedPolicyAsync(tenantId, "Editors");

        var first = await CreateRoleRepository(tenantId).AssignPolicyAsync(role, policy);
        var second = await CreateRoleRepository(tenantId).AssignPolicyAsync(role, policy);

        first.StatusCode.Should().Be(201);
        first.Data!.RoleId.Should().Be(role);
        first.Data.PolicyId.Should().Be(policy);
        second.StatusCode.Should().Be(200);
        second.Data!.Id.Should().Be(first.Data.Id);
    }

    [Fact]
    public async Task RemovePolicyAsync_ReturnsTheRemovedLink_ThenNotFound()
    {
        var tenantId = await NewTenantAsync();
        var role = await SeedRoleAsync(tenantId, "Editor");
        var policy = await SeedPolicyAsync(tenantId, "Editors");
        var link = (await CreateRoleRepository(tenantId).AssignPolicyAsync(role, policy)).Data!;

        var removed = await CreateRoleRepository(tenantId).RemovePolicyAsync(role, policy);
        var again = await CreateRoleRepository(tenantId).RemovePolicyAsync(role, policy);

        removed.Data.Should().Be(link);
        again.StatusCode.Should().Be(404);
    }

    // ── Permissions ───────────────────────────────────────────────

    [Fact]
    public async Task PermissionCatalogue_ListAllOrderedAndGetByKeysSkipsUnknown()
    {
        var b = Key("b");
        var a = Key("a");
        await SeedPermissionAsync(b, "B", "zz-test");
        await SeedPermissionAsync(a, "A", "zz-test");
        var permissions = CreatePermissionRepository();

        var all = (await permissions.ListAllAsync()).Data!;
        var found = (await permissions.GetByKeysAsync([a, "nope.nope.nope"])).Data!;

        all.Where(p => p.Module == "zz-test").Select(p => p.Key).Should().Equal(new[] { a, b }.Order(StringComparer.Ordinal));
        all.Select(p => p.Module).Should().BeInAscendingOrder(StringComparer.Ordinal);
        found.Select(p => p.Key).Should().Equal(a);
        (await permissions.GetByKeysAsync([])).Data.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static string Key(string action) => $"test.{Guid.NewGuid():N}.{action}";

    private Task<Guid> NewTenantAsync() => SeedTenantAsync("Org", $"org-{Guid.NewGuid():N}");

    private async Task<Guid> SeedPolicyAsync(Guid tenantId, string name)
    {
        var added = await CreatePolicyRepository(tenantId).AddAsync(new PolicyDto(Guid.Empty, name, null, tenantId));
        added.Success.Should().BeTrue(added.Message);
        return added.Data!.Id;
    }

    private async Task<Guid> SeedRoleAsync(Guid tenantId, string name)
    {
        var added = await CreateRoleRepository(tenantId).AddAsync(new RoleDto(Guid.Empty, name, null, RoleType.Application, tenantId, false));
        added.Success.Should().BeTrue(added.Message);
        return added.Data!.Id;
    }

    private async Task LinkPermissionAsync(Guid policyId, Guid permissionId)
    {
        DbContext.PolicyPermissions.Add(new PolicyPermission { PolicyId = policyId, PermissionId = permissionId });
        await DbContext.SaveChangesAsync();
    }

    /// <summary>Inserts a link directly, so links to another tenant's role (hand-edited data) can be made.</summary>
    private async Task LinkPolicyAsync(Guid roleId, Guid policyId)
    {
        DbContext.RolePolicies.Add(new RolePolicy { RoleId = roleId, PolicyId = policyId });
        await DbContext.SaveChangesAsync();
    }

    private Task<List<Guid>> LinkedPermissionsAsync(Guid policyId)
        => DbContext.PolicyPermissions.AsNoTracking().Where(pp => pp.PolicyId == policyId).Select(pp => pp.PermissionId).ToListAsync();
}
