using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Membership;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Roles;

/// <summary>
/// GU-68 Requirements 3 and 4: the single role writer. Uses the real grant policy, guard and access
/// revoker over substituted repositories.
/// </summary>
public sealed class UserRoleServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    private static readonly RoleDto Editor = new(Guid.NewGuid(), "Editor", null, RoleType.Application, TenantId, false);
    private static readonly RoleDto TenantAdmin = new(Guid.NewGuid(), AuthRoleNames.TenantAdmin, null, RoleType.Application, TenantId, true);
    private static readonly RoleDto SuperAdmin = new(Guid.NewGuid(), AuthRoleNames.SuperAdmin, null, RoleType.System, AuthRoleNames.SystemTenantId, true);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IUserTenantRepository _userTenants = Substitute.For<IUserTenantRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly ITokenRevocationStore _store = Substitute.For<ITokenRevocationStore>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly UserRoleService _sut;

    public UserRoleServiceTests()
    {
        _currentUser.UserId.Returns(CallerId);
        _tenantContext.TenantId.Returns(TenantId);

        foreach (var role in new[] { Editor, TenantAdmin, SuperAdmin })
        {
            _roles.FindByIdAnyTenantAsync(role.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(role));
        }

        _roles.FindByIdAnyTenantAsync(Arg.Is<Guid>(id => id != Editor.Id && id != TenantAdmin.Id && id != SuperAdmin.Id), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.NotFound());

        // The caller is TenantAdmin of the tenant by default
        _userRoles.GetSystemRolesForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(CallerId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(CallerId, TenantAdmin)]));

        GivenMembership(TargetId, TenantId, MembershipStatus.Active);
        GivenAssignments(TargetId, Editor);
        _userRoles.AddForTenantAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<UserRoleDto>.Ok(ci.ArgAt<UserRoleDto>(0) with { Id = Guid.NewGuid() }, statusCode: 201));

        _sut = new UserRoleService(
            _currentUser, _tenantContext, _userRoles, _userTenants, _roles, _users,
            new RoleGrantPolicy(_userRoles, GroundUp.Tests.Unit.Auth.TestHelpers.RoleRepositoryStubs.WithoutGrants(_roles), _permissions, _tenantContext, []),
            new LastAdminGuard(_userRoles),
            new TenantAccessRevoker(_store, _cache, NullLogger<TenantAccessRevoker>.Instance, _eventBus),
            NullLogger<UserRoleService>.Instance);
    }

    // ── Assign ────────────────────────────────────────────────────

    [Fact]
    public async Task AssignRoleAsync_NoUser_Unauthorized()
    {
        _currentUser.UserId.Returns(Guid.Empty);

        (await _sut.AssignRoleAsync(TargetId, Editor.Id)).StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task AssignRoleAsync_UnknownRole_NotFound()
        => (await _sut.AssignRoleAsync(TargetId, Guid.NewGuid())).StatusCode.Should().Be(404);

    [Fact]
    public async Task AssignRoleAsync_RoleOfAnotherTenant_NotFound()
    {
        var foreign = Editor with { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        _roles.FindByIdAnyTenantAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(foreign));

        (await _sut.AssignRoleAsync(TargetId, foreign.Id)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignRoleAsync_NoTenantSelected_TenantRequired()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.TenantRequired);
    }

    [Theory]
    [InlineData(MembershipStatus.Inactive)]
    [InlineData(null)]
    public async Task AssignRoleAsync_TargetNotAMember_NotFound(MembershipStatus? status)
    {
        if (status is null)
        {
            GivenNoMemberships(TargetId);
        }
        else
        {
            GivenMembership(TargetId, TenantId, status.Value);
        }

        (await _sut.AssignRoleAsync(TargetId, Editor.Id)).StatusCode.Should().Be(404);
        await _userRoles.DidNotReceiveWithAnyArgs().AddForTenantAsync(default!, default);
    }

    [Fact]
    public async Task AssignRoleAsync_PendingMember_GrantsTheRole()
    {
        GivenMembership(TargetId, TenantId, MembershipStatus.Pending);

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(201);
        await _userRoles.Received(1).AddForTenantAsync(
            Arg.Is<UserRoleDto>(r => r.UserId == TargetId && r.RoleId == Editor.Id && r.TenantId == TenantId && r.Source == RoleAssignmentSource.Manual),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRoleAsync_RulesRefuse_ForbiddenAndNothingSaved()
    {
        // The caller is a plain member: no admin role, no auth.roles.assign
        _userRoles.GetByUserIdForTenantAsync(CallerId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(403);
        await _userRoles.DidNotReceiveWithAnyArgs().AddForTenantAsync(default!, default);
    }

    [Fact]
    public async Task AssignRoleAsync_NewAssignment_PublishesEventForTheTargetAndEvictsTheCache()
    {
        _cache.Set(PermissionCacheKeys.For(TargetId, TenantId), new HashSet<string>());

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(201);
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityCreatedEvent<UserRoleDto>>(e => e.UserId == TargetId && e.TenantId == TenantId && e.Entity.RoleId == Editor.Id),
            Arg.Any<CancellationToken>());
        _cache.TryGetValue(PermissionCacheKeys.For(TargetId, TenantId), out _).Should().BeFalse();
        await _store.DidNotReceiveWithAnyArgs().RevokeTenantAccessAsync(default, default, default);
    }

    [Fact]
    public async Task AssignRoleAsync_AlreadyHeldFromThisSource_NoChange()
    {
        GivenAssignments(TargetId, Editor, Row(TargetId, Editor, RoleAssignmentSource.Manual));

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(200);
        await _userRoles.DidNotReceiveWithAnyArgs().AddForTenantAsync(default!, default);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<UserRoleDto>>(default!, default);
    }

    [Fact]
    public async Task AssignRoleAsync_HeldFromAnotherSourceOnly_AddsAManualRow()
    {
        GivenAssignments(TargetId, Editor, Row(TargetId, Editor, RoleAssignmentSource.Invitation));

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task AssignRoleAsync_ConcurrentInsertWins_ReportsTheExistingRow()
    {
        var existing = Row(TargetId, Editor, RoleAssignmentSource.Manual);
        _userRoles.GetAssignmentsAsync(TargetId, Editor.Id, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]), OperationResult<List<UserRoleDto>>.Ok([existing]));
        _userRoles.AddForTenantAsync(Arg.Any<UserRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserRoleDto>.Fail("exists", 409));

        var result = await _sut.AssignRoleAsync(TargetId, Editor.Id);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(existing.Id);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<UserRoleDto>>(default!, default);
    }

    [Fact]
    public async Task AssignRoleAsync_SuperAdminByASuperAdmin_CreatesTheSystemTenantMembership()
    {
        GivenSuperAdminCaller();
        GivenNoMemberships(TargetId);
        GivenAssignments(TargetId, SuperAdmin);
        _users.GetByIdAsync(TargetId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<UserDto>.Ok(new UserDto(TargetId, "ext-target", "t@test.com", "T", true)));
        _userTenants.AddForTenantAsync(Arg.Any<UserTenantDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => OperationResult<UserTenantDto>.Ok(ci.ArgAt<UserTenantDto>(0)));

        var result = await _sut.AssignRoleAsync(TargetId, SuperAdmin.Id);

        result.StatusCode.Should().Be(201);
        await _userTenants.Received(1).AddForTenantAsync(
            Arg.Is<UserTenantDto>(m => m.UserId == TargetId && m.TenantId == AuthRoleNames.SystemTenantId
                && m.Status == MembershipStatus.Active && m.ExternalUserId == "ext-target"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignRoleAsync_SuperAdminByATenantAdmin_Forbidden()
    {
        var result = await _sut.AssignRoleAsync(TargetId, SuperAdmin.Id);

        result.StatusCode.Should().Be(403);
        await _userTenants.DidNotReceiveWithAnyArgs().AddForTenantAsync(default!, default);
    }

    // ── Remove ────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRoleAsync_RemovesEverySource_PublishesPerRowAndRevokes()
    {
        var rows = new List<UserRoleDto>
        {
            Row(TargetId, Editor, RoleAssignmentSource.Manual),
            Row(TargetId, Editor, RoleAssignmentSource.Invitation)
        };
        _userRoles.RemoveAssignmentsAsync(TargetId, Editor.Id, TenantId, null, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(rows));
        _cache.Set(PermissionCacheKeys.For(TargetId, TenantId), new HashSet<string>());

        var result = await _sut.RemoveRoleAsync(TargetId, Editor.Id);

        result.Success.Should().BeTrue();
        await _eventBus.Received(2).PublishAsync(
            Arg.Is<EntityDeletedEvent<UserRoleDto>>(e => e.UserId == TargetId && e.TenantId == TenantId),
            Arg.Any<CancellationToken>());
        await _store.Received(1).RevokeTenantAccessAsync(TargetId, TenantId, Arg.Any<CancellationToken>());
        _cache.TryGetValue(PermissionCacheKeys.For(TargetId, TenantId), out _).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveRoleAsync_NothingHeld_NoChangeAndNoRevocation()
    {
        _userRoles.RemoveAssignmentsAsync(TargetId, Editor.Id, TenantId, null, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([]));

        var result = await _sut.RemoveRoleAsync(TargetId, Editor.Id);

        result.Success.Should().BeTrue();
        await _store.DidNotReceiveWithAnyArgs().RevokeTenantAccessAsync(default, default, default);
    }

    [Fact]
    public async Task RemoveRoleAsync_LastTenantAdmin_ConflictAndNothingRemoved()
    {
        _userRoles.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(TargetId, TenantAdmin)]));

        var result = await _sut.RemoveRoleAsync(TargetId, TenantAdmin.Id);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.LastAdmin);
        await _userRoles.DidNotReceiveWithAnyArgs().RemoveAssignmentsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task RemoveRoleAsync_LastSuperAdminRemovingThemself_Conflict()
    {
        GivenSuperAdminCaller();
        _userRoles.GetActiveHolderIdsAsync(SuperAdmin.Id, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<Guid>>.Ok([CallerId]));

        var result = await _sut.RemoveRoleAsync(CallerId, SuperAdmin.Id);

        result.ErrorCode.Should().Be(AuthErrorCodes.LastSuperAdmin);
        await _userRoles.DidNotReceiveWithAnyArgs().RemoveAssignmentsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task RemoveRoleAsync_RulesRefuse_Forbidden()
    {
        // A TenantAdmin cannot remove SuperAdmin
        var result = await _sut.RemoveRoleAsync(TargetId, SuperAdmin.Id);

        result.StatusCode.Should().Be(403);
    }

    // ── System path ───────────────────────────────────────────────

    [Fact]
    public async Task GrantAsync_SkipsCallerRules_AndKeepsTheSource()
    {
        // Even with no signed-in user (sign-in callback, provisioning)
        _currentUser.UserId.Returns(Guid.Empty);

        var result = await ((IRoleGrantWriter)_sut).GrantAsync(TargetId, Editor.Id, TenantId, RoleAssignmentSource.JoinLink);

        result.Success.Should().BeTrue();
        result.Data!.Created.Should().BeTrue();
        result.Data.Assignment.Source.Should().Be(RoleAssignmentSource.JoinLink);
        await _eventBus.Received(1).PublishAsync(Arg.Any<EntityCreatedEvent<UserRoleDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GrantAsync_Deferred_PublishesOnlyWhenAsked()
    {
        IRoleGrantWriter writer = _sut;

        var result = await writer.GrantAsync(TargetId, Editor.Id, TenantId, RoleAssignmentSource.Provisioning, deferEvents: true);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<UserRoleDto>>(default!, default);

        await writer.PublishGrantedAsync(result.Data!);
        await _eventBus.Received(1).PublishAsync(Arg.Any<EntityCreatedEvent<UserRoleDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GrantAsync_RoleOfAnotherTenant_NotFound()
    {
        var result = await ((IRoleGrantWriter)_sut).GrantAsync(TargetId, Editor.Id, Guid.NewGuid(), RoleAssignmentSource.DefaultRole);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RemoveBySourceAsync_RemovesOnlyThatSource()
    {
        var invitationRow = Row(TargetId, Editor, RoleAssignmentSource.Invitation);
        _userRoles.RemoveAssignmentsAsync(TargetId, null, TenantId, RoleAssignmentSource.Invitation, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([invitationRow]));

        var result = await ((IRoleGrantWriter)_sut).RemoveBySourceAsync(TargetId, TenantId, RoleAssignmentSource.Invitation);

        result.Success.Should().BeTrue();
        await _userRoles.Received(1).RemoveAssignmentsAsync(TargetId, null, TenantId, RoleAssignmentSource.Invitation, Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(Arg.Is<EntityDeletedEvent<UserRoleDto>>(e => e.EntityId == invitationRow.Id), Arg.Any<CancellationToken>());
    }

    // ── CanGrantRoleAsync ─────────────────────────────────────────

    [Fact]
    public async Task CanGrantRoleAsync_ReflectsTheRules()
    {
        (await _sut.CanGrantRoleAsync(Editor.Id)).Data.Should().BeTrue();
        (await _sut.CanGrantRoleAsync(SuperAdmin.Id)).Data.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static UserRoleDto Row(Guid userId, RoleDto role, RoleAssignmentSource source = RoleAssignmentSource.Manual)
        => new(Guid.NewGuid(), userId, role.Id, role.TenantId, role.Name, source);

    private void GivenMembership(Guid userId, Guid tenantId, MembershipStatus status)
        => _userTenants.GetAllMembershipsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok([new UserTenantDto(Guid.NewGuid(), userId, tenantId, "ext", status)]));

    private void GivenNoMemberships(Guid userId)
        => _userTenants.GetAllMembershipsForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok([]));

    private void GivenAssignments(Guid userId, RoleDto role, params UserRoleDto[] rows)
        => _userRoles.GetAssignmentsAsync(userId, role.Id, role.TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(rows.ToList()));

    private void GivenSuperAdminCaller()
        => _userRoles.GetSystemRolesForUserAsync(CallerId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(CallerId, SuperAdmin)]));
}
