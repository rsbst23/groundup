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
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// GU-68 Requirement 5.1-5.2: listing and removing members. Permissions are enforced by the
/// authorization proxy (covered by the startup check and integration tests); these tests cover the
/// per-role rules, the last-admin guard and the removal steps.
/// </summary>
public sealed class MemberManagementServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly RoleDto Editor = new(Guid.NewGuid(), "Editor", null, RoleType.Application, TenantId, false);
    private static readonly RoleDto TenantAdmin = new(Guid.NewGuid(), AuthRoleNames.TenantAdmin, null, RoleType.Application, TenantId, true);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IUserTenantRepository _userTenants = Substitute.For<IUserTenantRepository>();
    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly ITokenRevocationStore _store = Substitute.For<ITokenRevocationStore>();
    private readonly MemberManagementService _sut;

    public MemberManagementServiceTests()
    {
        _currentUser.UserId.Returns(CallerId);
        _tenantContext.TenantId.Returns(TenantId);
        _roles.FindByIdAnyTenantAsync(Editor.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(Editor));
        _roles.FindByIdAnyTenantAsync(TenantAdmin.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(TenantAdmin));

        // The caller holds auth.roles.assign but is not an admin
        _userRoles.GetSystemRolesForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(true);

        _userRoles.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(CallerId, TenantAdmin), Row(Guid.NewGuid(), TenantAdmin)]));
        GivenMembership(MembershipStatus.Active);
        GivenRoles(Row(MemberId, Editor));
        _userTenants.DeactivateMembershipAsync(MemberId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(MemberId, Editor)]));

        _sut = new MemberManagementService(
            _currentUser, _tenantContext, _userTenants, _userRoles, _roles,
            new RoleGrantPolicy(_userRoles, _permissions, _tenantContext, []),
            new LastAdminGuard(_userRoles),
            new TenantAccessRevoker(_store, new MemoryCache(new MemoryCacheOptions()), NullLogger<TenantAccessRevoker>.Instance),
            NullLogger<MemberManagementService>.Instance);
    }

    [Fact]
    public async Task ListMembersAsync_NoTenantSelected_TenantRequired()
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        var result = await _sut.ListMembersAsync(new PaginationParams());

        result.ErrorCode.Should().Be(GroundUp.Core.ErrorCodes.TenantRequired);
    }

    [Fact]
    public async Task ListMembersAsync_ReadsTheCurrentTenant()
    {
        var page = new PaginatedData<TenantMemberDto> { Items = [], PageNumber = 1, PageSize = 10, TotalRecords = 0 };
        _userTenants.GetMembersAsync(TenantId, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<PaginatedData<TenantMemberDto>>.Ok(page));

        var result = await _sut.ListMembersAsync(new PaginationParams());

        result.Data.Should().BeSameAs(page);
    }

    [Theory]
    [InlineData(MembershipStatus.Active)]
    [InlineData(MembershipStatus.Pending)]
    public async Task RemoveMemberAsync_ActiveOrPendingMember_DeactivatesAndEndsAccess(MembershipStatus status)
    {
        GivenMembership(status);

        var result = await _sut.RemoveMemberAsync(MemberId);

        result.Success.Should().BeTrue();
        await _userTenants.Received(1).DeactivateMembershipAsync(MemberId, TenantId, Arg.Any<CancellationToken>());
        await _store.Received(1).RevokeTenantAccessAsync(MemberId, TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMemberAsync_NotAMember_NotFound()
    {
        GivenMembership(MembershipStatus.Inactive);

        (await _sut.RemoveMemberAsync(MemberId)).StatusCode.Should().Be(404);
        await _userTenants.DidNotReceiveWithAnyArgs().DeactivateMembershipAsync(default, default, default);
    }

    [Fact]
    public async Task RemoveMemberAsync_MemberIsTenantAdminAndCallerIsNot_Forbidden()
    {
        // auth.members.manage alone must not let someone remove a TenantAdmin
        GivenRoles(Row(MemberId, TenantAdmin));

        var result = await _sut.RemoveMemberAsync(MemberId);

        result.StatusCode.Should().Be(403);
        result.Message.Should().Contain(AuthRoleNames.TenantAdmin);
        await _userTenants.DidNotReceiveWithAnyArgs().DeactivateMembershipAsync(default, default, default);
    }

    [Fact]
    public async Task RemoveMemberAsync_LastTenantAdmin_Conflict()
    {
        // The caller is a TenantAdmin, and the member being removed is the tenant's only active TenantAdmin
        _userRoles.GetByUserIdForTenantAsync(CallerId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(CallerId, TenantAdmin)]));
        _userRoles.GetTenantAdminHoldersAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok([Row(MemberId, TenantAdmin)]));
        GivenRoles(Row(MemberId, TenantAdmin));

        var result = await _sut.RemoveMemberAsync(MemberId);

        result.ErrorCode.Should().Be(AuthErrorCodes.LastAdmin);
        await _userTenants.DidNotReceiveWithAnyArgs().DeactivateMembershipAsync(default, default, default);
    }

    private void GivenMembership(MembershipStatus status)
        => _userTenants.GetAllMembershipsForUserAsync(MemberId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserTenantDto>>.Ok([new UserTenantDto(Guid.NewGuid(), MemberId, TenantId, "ext", status)]));

    private void GivenRoles(params UserRoleDto[] rows)
        => _userRoles.GetAllForTenantAsync(MemberId, TenantId, Arg.Any<CancellationToken>())
            .Returns(OperationResult<List<UserRoleDto>>.Ok(rows.ToList()));

    private static UserRoleDto Row(Guid userId, RoleDto role) => new(Guid.NewGuid(), userId, role.Id, role.TenantId, role.Name);
}
