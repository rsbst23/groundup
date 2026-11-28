using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Configuration;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// GU-96: cached permission sets carry the tenant's version, so a bump makes every user of the tenant
/// reload; and the permission summary used by <c>/auth/me</c> and the escalation guard.
/// </summary>
public sealed class PermissionServiceVersionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();

    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPolicyRepository _policies = Substitute.For<IPolicyRepository>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly PermissionCacheVersions _versions = new();
    private readonly PermissionService _sut;

    public PermissionServiceVersionTests()
    {
        _tenantContext.TenantId.Returns(TenantId);
        _userRoles.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, RoleId, TenantId, "Editor")]));
        _userRoles.GetSystemRolesForUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, RoleId, TenantId, "Editor")]));
        _roles.GetPoliciesForRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PolicyDto>>.Ok([new PolicyDto(PolicyId, "Editors", null, TenantId)]));
        GivenPolicyKeys("docs.write", "docs.read");

        _sut = new PermissionService(
            _userRoles, _roles, _policies, _tenantContext,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { PermissionCacheTtlMinutes = 15 }),
            _versions);
    }

    // ── Versions ──────────────────────────────────────────────────

    [Fact]
    public async Task GetUserPermissionsAsync_TenantBumped_Reloads()
    {
        await _sut.GetUserPermissionsAsync(UserId);
        GivenPolicyKeys("docs.read");

        _versions.Bump(TenantId);
        var after = await _sut.GetUserPermissionsAsync(UserId);

        after.Should().BeEquivalentTo("docs.read");
        await _userRoles.Received(2).GetByUserIdAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_NoBump_UsesTheCache()
    {
        await _sut.GetUserPermissionsAsync(UserId);
        GivenPolicyKeys("docs.read");

        var after = await _sut.GetUserPermissionsAsync(UserId);

        after.Should().BeEquivalentTo("docs.read", "docs.write");
        await _userRoles.Received(1).GetByUserIdAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_AnotherTenantBumped_UsesTheCache()
    {
        await _sut.GetUserPermissionsAsync(UserId);

        _versions.Bump(Guid.NewGuid());
        await _sut.GetUserPermissionsAsync(UserId);

        await _userRoles.Received(1).GetByUserIdAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPermissionsAsync_BumpDuringTheLoad_StoredSetIsStale()
    {
        // A change saved while the first load runs: the version is read before loading, so the set
        // stored by that load must not be trusted afterwards
        _userRoles.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _versions.Bump(TenantId);
                return OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, RoleId, TenantId, "Editor")]);
            }, _ => OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, RoleId, TenantId, "Editor")]));

        await _sut.GetUserPermissionsAsync(UserId);
        await _sut.GetUserPermissionsAsync(UserId);
        await _sut.GetUserPermissionsAsync(UserId);

        await _userRoles.Received(2).GetByUserIdAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Versions_StartAtZeroAndIncrementPerTenant()
    {
        var other = Guid.NewGuid();

        _versions.Current(TenantId).Should().Be(0);
        _versions.Bump(TenantId).Should().Be(1);
        _versions.Bump(TenantId).Should().Be(2);
        _versions.Current(other).Should().Be(0);
    }

    // ── Summary ───────────────────────────────────────────────────

    [Fact]
    public async Task GetPermissionSummaryAsync_Member_SortedKeys()
    {
        var summary = await _sut.GetPermissionSummaryAsync(UserId);

        summary.Should().BeEquivalentTo(new PermissionSummaryDto(false, ["docs.read", "docs.write"]), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task GetPermissionSummaryAsync_TenantAdmin_AllPermissions()
    {
        _userRoles.GetByUserIdForTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, Guid.NewGuid(), TenantId, AuthRoleNames.TenantAdmin)]));

        var summary = await _sut.GetPermissionSummaryAsync(UserId);

        summary.AllPermissions.Should().BeTrue();
        summary.Permissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPermissionSummaryAsync_SuperAdmin_AllPermissionsWithOrWithoutATenant(bool hasTenant)
    {
        if (!hasTenant)
        {
            _tenantContext.TenantId.Returns(Guid.Empty);
        }

        _userRoles.GetSystemRolesForUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([new UserRoleDto(Guid.NewGuid(), UserId, Guid.NewGuid(), AuthRoleNames.SystemTenantId, AuthRoleNames.SuperAdmin)]));

        (await _sut.GetPermissionSummaryAsync(UserId)).AllPermissions.Should().BeTrue();
    }

    [Fact]
    public async Task GetPermissionSummaryAsync_NoTenant_Nothing()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        var summary = await _sut.GetPermissionSummaryAsync(UserId);

        summary.Should().BeEquivalentTo(new PermissionSummaryDto(false, []));
        await _userRoles.DidNotReceiveWithAnyArgs().GetByUserIdAsync(default, default);
    }

    [Fact]
    public async Task GetPermissionSummaryAsync_NoUser_NothingWithoutReading()
    {
        var summary = await _sut.GetPermissionSummaryAsync(Guid.Empty);

        summary.AllPermissions.Should().BeFalse();
        summary.Permissions.Should().BeEmpty();
        await _userRoles.DidNotReceiveWithAnyArgs().GetSystemRolesForUserAsync(default, default);
    }

    private void GivenPolicyKeys(params string[] keys)
        => _policies.GetPermissionsForPolicyAsync(PolicyId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<PermissionDto>>.Ok(keys.Select(k => new PermissionDto(Guid.NewGuid(), k, k, null, "docs")).ToList()));
}
