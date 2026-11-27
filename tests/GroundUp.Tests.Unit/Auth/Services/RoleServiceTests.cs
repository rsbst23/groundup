using System.Reflection;
using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services;
using GroundUp.Auth.Services.Roles;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Attributes;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Data.Abstractions;
using GroundUp.Events;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CoreErrorCodes = GroundUp.Core.ErrorCodes;

namespace GroundUp.Tests.Unit.Auth.Services;

/// <summary>
/// GU-95: the role service's rules. Permissions are enforced by the authorization proxy (the
/// attributes are checked here, the proxy by its own tests and the integration tests).
/// </summary>
public sealed class RoleServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly RoleDto Editor = new(Guid.NewGuid(), "Editor", "Edits", RoleType.Application, TenantId, false);
    private static readonly RoleDto TenantAdmin = new(Guid.NewGuid(), AuthRoleNames.TenantAdmin, null, RoleType.Application, TenantId, true);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUserRoleRepository _userRoles = Substitute.For<IUserRoleRepository>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly RoleService _sut;

    public RoleServiceTests()
    {
        _currentUser.UserId.Returns(CallerId);
        _tenantContext.TenantId.Returns(TenantId);
        _roles.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.NotFound());
        GivenRole(Editor);
        GivenRole(TenantAdmin);
        _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(OperationResult<Guid?>.Ok(null));
        _tenants.SetDefaultRoleIdAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());
        GivenUsage(new RoleUsageDto(0, 0, 0, false));
        _roles.DeleteAsync(Editor.Id, Arg.Any<CancellationToken>()).Returns(OperationResult.Ok());

        // The unit of work runs the operation; an exception thrown inside propagates, as in the real one
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => RunAsync(call.Arg<Func<CancellationToken, Task>>()));

        // The caller holds auth.roles.assign but is not an admin
        _userRoles.GetSystemRolesForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _userRoles.GetByUserIdForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OperationResult<List<UserRoleDto>>.Ok([]));
        _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(true);

        _sut = new RoleService(
            _currentUser, _tenantContext, _roles, _tenants,
            new RoleGrantPolicy(_userRoles, _permissions, _tenantContext, []),
            _unitOfWork, new CreateRoleDtoValidator(), new UpdateRoleDtoValidator(),
            NullLogger<RoleService>.Instance, _eventBus);
    }

    // ── Current tenant ────────────────────────────────────────────

    public static TheoryData<string> AllMethods => new()
    {
        "List", "Get", "Create", "Update", "Delete", "GetDefault", "SetDefault", "ClearDefault"
    };

    [Theory]
    [MemberData(nameof(AllMethods))]
    public async Task AnyMethod_NoTenantSelected_TenantRequired(string method)
    {
        _tenantContext.TenantId.Returns(Guid.Empty);

        var (status, code) = await CallAsync(method);

        status.Should().Be(403);
        code.Should().Be(CoreErrorCodes.TenantRequired);
    }

    [Fact]
    public async Task ListAsync_SystemTenant_EmptyWithoutReading()
    {
        _tenantContext.TenantId.Returns(AuthRoleNames.SystemTenantId);

        var result = await _sut.ListAsync(new PaginationParams());

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        await _roles.DidNotReceiveWithAnyArgs().GetAllAsync(default!, default);
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("SetDefault")]
    public async Task RoleIdMethods_SystemTenant_NotFound(string method)
    {
        _tenantContext.TenantId.Returns(AuthRoleNames.SystemTenantId);

        var (status, _) = await CallAsync(method);

        status.Should().Be(404);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("ClearDefault")]
    public async Task TenantWrites_SystemTenant_Forbidden(string method)
    {
        _tenantContext.TenantId.Returns(AuthRoleNames.SystemTenantId);

        var (status, _) = await CallAsync(method);

        status.Should().Be(403);
    }

    // ── List and get ──────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MarksTheDefaultRoleAndSortsByName()
    {
        GivenDefault(Editor.Id);
        _roles.GetAllAsync(Arg.Any<FilterParams>(), Arg.Any<CancellationToken>()).Returns(OperationResult<PaginatedData<RoleDto>>.Ok(
            new PaginatedData<RoleDto> { Items = [Editor, TenantAdmin], PageNumber = 1, PageSize = 10, TotalRecords = 2 }));

        var result = await _sut.ListAsync(new PaginationParams());

        result.Data!.Items.Select(r => (r.Name, r.IsDefault)).Should().Equal(("Editor", true), (AuthRoleNames.TenantAdmin, false));
        result.Data.TotalRecords.Should().Be(2);
        await _roles.Received(1).GetAllAsync(Arg.Is<FilterParams>(f => f.SortBy == "Name"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_RoleOfAnotherTenant_NotFound()
    {
        var other = Editor with { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() };
        GivenRole(other);

        (await _sut.GetAsync(other.Id)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetAsync_DefaultRole_IsDefault()
    {
        GivenDefault(Editor.Id);

        var result = await _sut.GetAsync(Editor.Id);

        result.Data.Should().Be(TenantRoleDto.From(Editor, isDefault: true));
    }

    // ── Create ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SystemType_Refused()
    {
        var result = await _sut.CreateAsync(new CreateRoleDto("Ops", null, RoleType.System));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(CoreErrorCodes.ValidationFailed);
        await _roles.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyName_ValidationFailed(string name)
    {
        var result = await _sut.CreateAsync(new CreateRoleDto(name, null, RoleType.Application));

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be(CoreErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task CreateAsync_NameTooLong_ValidationFailed()
        => (await _sut.CreateAsync(new CreateRoleDto(new string('a', 201), null, RoleType.Application))).StatusCode.Should().Be(400);

    [Fact]
    public async Task CreateAsync_Valid_TrimsAddsAndPublishes()
    {
        var created = new RoleDto(Guid.NewGuid(), "Viewer", null, RoleType.Workspace, TenantId, false);
        _roles.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(created, "Created", 201));

        var result = await _sut.CreateAsync(new CreateRoleDto("  Viewer ", "  ", RoleType.Workspace));

        result.StatusCode.Should().Be(201);
        result.Data.Should().Be(TenantRoleDto.From(created, isDefault: false));
        await _roles.Received(1).AddAsync(
            Arg.Is<RoleDto>(r => r.Name == "Viewer" && r.Description == null && r.RoleType == RoleType.Workspace && r.TenantId == TenantId && !r.IsSystem),
            Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityCreatedEvent<RoleDto>>(e => e.Entity == created && e.TenantId == TenantId && e.UserId == CallerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_RepositoryConflict_RoleNameTaken()
    {
        _roles.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.Fail("conflict", 409, CoreErrorCodes.Conflict));

        var result = await _sut.CreateAsync(new CreateRoleDto("editor", null, RoleType.Application));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.RoleNameTaken);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityCreatedEvent<RoleDto>>(default!, default);
    }

    [Fact]
    public async Task CreateAsync_EventHandlerFails_StillSucceeds()
    {
        _roles.AddAsync(Arg.Any<RoleDto>(), Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(Editor, "Created", 201));
        _eventBus.PublishAsync(Arg.Any<EntityCreatedEvent<RoleDto>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("handler")));

        (await _sut.CreateAsync(new CreateRoleDto("Editor", null, RoleType.Application))).Success.Should().BeTrue();
    }

    // ── Update ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Administrators")]
    [InlineData("tenantadmin")]
    [InlineData("TENANTADMIN")]
    public async Task UpdateAsync_BuiltInRoleWithAnotherName_RoleIsBuiltIn(string name)
    {
        var result = await _sut.UpdateAsync(TenantAdmin.Id, new UpdateRoleDto(name, null));

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.RoleIsBuiltIn);
        await _roles.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
    }

    [Fact]
    public async Task UpdateAsync_BuiltInRoleSameNameNewDescription_Updates()
    {
        _roles.UpdateAsync(TenantAdmin.Id, Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(call => OperationResult<RoleDto>.Ok(call.Arg<RoleDto>()));

        var result = await _sut.UpdateAsync(TenantAdmin.Id, new UpdateRoleDto($" {AuthRoleNames.TenantAdmin} ", "Runs the organization"));

        result.Success.Should().BeTrue();
        result.Data!.Description.Should().Be("Runs the organization");
        await _roles.Received(1).UpdateAsync(
            TenantAdmin.Id,
            TenantAdmin with { Description = "Runs the organization" },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Rename_KeepsTypeAndTenantAndPublishes()
    {
        _roles.UpdateAsync(Editor.Id, Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(call => OperationResult<RoleDto>.Ok(call.Arg<RoleDto>()));
        GivenDefault(Editor.Id);

        var result = await _sut.UpdateAsync(Editor.Id, new UpdateRoleDto("Writer", null));

        result.Data.Should().Be(TenantRoleDto.From(Editor with { Name = "Writer", Description = null }, isDefault: true));
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityUpdatedEvent<RoleDto>>(e => e.Entity.Name == "Writer" && e.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RepositoryConflict_RoleNameTaken()
    {
        _roles.UpdateAsync(Editor.Id, Arg.Any<RoleDto>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<RoleDto>.Fail("conflict", 409, CoreErrorCodes.Conflict));

        var result = await _sut.UpdateAsync(Editor.Id, new UpdateRoleDto("Viewer", null));

        result.ErrorCode.Should().Be(AuthErrorCodes.RoleNameTaken);
    }

    [Fact]
    public async Task UpdateAsync_InvalidName_ValidationFailed()
        => (await _sut.UpdateAsync(Editor.Id, new UpdateRoleDto(" ", null))).ErrorCode.Should().Be(CoreErrorCodes.ValidationFailed);

    [Fact]
    public async Task UpdateAsync_UnknownRole_NotFound()
        => (await _sut.UpdateAsync(Guid.NewGuid(), new UpdateRoleDto("X", null))).StatusCode.Should().Be(404);

    // ── Delete ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_BuiltInRole_RoleIsBuiltIn()
    {
        var result = await _sut.DeleteAsync(TenantAdmin.Id);

        result.ErrorCode.Should().Be(AuthErrorCodes.RoleIsBuiltIn);
        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
    }

    [Theory]
    [InlineData(3, 0, 0, false, "Held by 3 members.")]
    [InlineData(1, 0, 0, false, "Held by 1 member.")]
    [InlineData(0, 1, 0, false, "Used by 1 pending or expired invitation.")]
    [InlineData(0, 2, 0, false, "Used by 2 pending or expired invitations.")]
    [InlineData(0, 0, 2, false, "Used by 2 active join links.")]
    [InlineData(0, 0, 1, false, "Used by 1 active join link.")]
    [InlineData(0, 0, 0, true, "It is the organization's default role for new members.")]
    public async Task DeleteAsync_InUse_RoleInUseWithTheReason(int holders, int invitations, int links, bool isDefault, string reason)
    {
        GivenUsage(new RoleUsageDto(holders, invitations, links, isDefault));

        var result = await _sut.DeleteAsync(Editor.Id);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.RoleInUse);
        result.Errors.Should().Equal(reason);
        await _roles.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task DeleteAsync_SeveralUses_ListsEveryReason()
    {
        GivenUsage(new RoleUsageDto(2, 1, 1, true));

        var result = await _sut.DeleteAsync(Editor.Id);

        result.Errors.Should().HaveCount(4);
    }

    [Fact]
    public async Task DeleteAsync_Unused_DeletesInTheTransactionAndPublishes()
    {
        var result = await _sut.DeleteAsync(Editor.Id);

        result.Success.Should().BeTrue();
        Received.InOrder(() =>
        {
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
            _roles.GetUsageAsync(Editor.Id, Arg.Any<CancellationToken>());
            _roles.DeleteAsync(Editor.Id, Arg.Any<CancellationToken>());
        });
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityDeletedEvent<RoleDto>>(e => e.EntityId == Editor.Id && e.TenantId == TenantId && e.UserId == CallerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ForeignKeyRejectsTheDelete_RoleInUse()
    {
        _roles.DeleteAsync(Editor.Id, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("in use", 409, CoreErrorCodes.Conflict));

        var result = await _sut.DeleteAsync(Editor.Id);

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(AuthErrorCodes.RoleInUse);
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityDeletedEvent<RoleDto>>(default!, default);
    }

    [Fact]
    public async Task DeleteAsync_TransactionFails_ReturnsItsResult()
    {
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("Unique constraint violation", 409));

        (await _sut.DeleteAsync(Editor.Id)).Success.Should().BeFalse();
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<EntityDeletedEvent<RoleDto>>(default!, default);
    }

    // ── Default role ──────────────────────────────────────────────

    [Fact]
    public async Task GetDefaultAsync_None_NullData()
    {
        var result = await _sut.GetDefaultAsync();

        result.Success.Should().BeTrue();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultAsync_Set_ReturnsTheRole()
    {
        GivenDefault(Editor.Id);

        (await _sut.GetDefaultAsync()).Data.Should().Be(TenantRoleDto.From(Editor, isDefault: true));
    }

    [Fact]
    public async Task SetDefaultAsync_BuiltInRole_RoleIsBuiltIn()
    {
        var result = await _sut.SetDefaultAsync(TenantAdmin.Id);

        result.ErrorCode.Should().Be(AuthErrorCodes.RoleIsBuiltIn);
        await _tenants.DidNotReceiveWithAnyArgs().SetDefaultRoleIdAsync(default, default, default);
    }

    [Fact]
    public async Task SetDefaultAsync_CallerMayNotGrantTheRole_PolicyRefusal()
    {
        _permissions.HasPermissionAsync(CallerId, PermissionKeysAuth.RolesAssign, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.SetDefaultAsync(Editor.Id);

        result.StatusCode.Should().Be(403);
        await _tenants.DidNotReceiveWithAnyArgs().SetDefaultRoleIdAsync(default, default, default);
    }

    [Fact]
    public async Task SetDefaultAsync_RuleRefuses_PassesItsResultThrough()
    {
        var rule = Substitute.For<IRoleAssignmentRule>();
        rule.EvaluateAsync(Arg.Any<RoleAssignmentContext>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult.Fail("No defaults on Sundays", 403, "CUSTOM"));
        var sut = new RoleService(
            _currentUser, _tenantContext, _roles, _tenants,
            new RoleGrantPolicy(_userRoles, _permissions, _tenantContext, [rule]),
            _unitOfWork, new CreateRoleDtoValidator(), new UpdateRoleDtoValidator(),
            NullLogger<RoleService>.Instance, _eventBus);

        var result = await sut.SetDefaultAsync(Editor.Id);

        result.ErrorCode.Should().Be("CUSTOM");
        await rule.Received(1).EvaluateAsync(
            Arg.Is<RoleAssignmentContext>(c => c.Source == RoleAssignmentSource.DefaultRole && c.Change == RoleChange.Assign),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDefaultAsync_Allowed_SetsAndPublishes()
    {
        var result = await _sut.SetDefaultAsync(Editor.Id);

        result.Data.Should().Be(TenantRoleDto.From(Editor, isDefault: true));
        await _tenants.Received(1).SetDefaultRoleIdAsync(TenantId, Editor.Id, Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityUpdatedEvent<RoleDto>>(e => e.Entity == Editor), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearDefaultAsync_Set_ClearsAndPublishes()
    {
        GivenDefault(Editor.Id);

        (await _sut.ClearDefaultAsync()).Success.Should().BeTrue();

        await _tenants.Received(1).SetDefaultRoleIdAsync(TenantId, null, Arg.Any<CancellationToken>());
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<EntityUpdatedEvent<RoleDto>>(e => e.Entity == Editor), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearDefaultAsync_None_SucceedsWithoutWriting()
    {
        (await _sut.ClearDefaultAsync()).Success.Should().BeTrue();

        await _tenants.DidNotReceiveWithAnyArgs().SetDefaultRoleIdAsync(default, default, default);
    }

    [Fact]
    public async Task NoEventBus_ChangesStillSucceed()
    {
        var sut = new RoleService(
            _currentUser, _tenantContext, _roles, _tenants,
            new RoleGrantPolicy(_userRoles, _permissions, _tenantContext, []),
            _unitOfWork, new CreateRoleDtoValidator(), new UpdateRoleDtoValidator(),
            NullLogger<RoleService>.Instance);

        (await sut.DeleteAsync(Editor.Id)).Success.Should().BeTrue();
        (await sut.SetDefaultAsync(Editor.Id)).Success.Should().BeTrue();
    }

    // ── Permissions on the interface ──────────────────────────────

    [Theory]
    [InlineData(nameof(IRoleService.ListAsync), PermissionKeysAuth.RolesRead)]
    [InlineData(nameof(IRoleService.GetAsync), PermissionKeysAuth.RolesRead)]
    [InlineData(nameof(IRoleService.GetDefaultAsync), PermissionKeysAuth.RolesRead)]
    [InlineData(nameof(IRoleService.CreateAsync), PermissionKeysAuth.RolesWrite)]
    [InlineData(nameof(IRoleService.UpdateAsync), PermissionKeysAuth.RolesWrite)]
    [InlineData(nameof(IRoleService.SetDefaultAsync), PermissionKeysAuth.RolesWrite)]
    [InlineData(nameof(IRoleService.ClearDefaultAsync), PermissionKeysAuth.RolesWrite)]
    [InlineData(nameof(IRoleService.DeleteAsync), PermissionKeysAuth.RolesDelete)]
    public void Interface_MethodRequiresPermission(string method, string permission)
    {
        var attribute = typeof(IRoleService).GetMethod(method)!.GetCustomAttribute<RequiresPermissionAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permissions.Should().Equal(permission);
    }

    [Fact]
    public void Interface_EveryMethodHasAPermission()
        => typeof(IRoleService).GetMethods()
            .Should().OnlyContain(m => m.GetCustomAttribute<RequiresPermissionAttribute>() != null);

    // ── Helpers ───────────────────────────────────────────────────

    private async Task<(int Status, string? Code)> CallAsync(string method)
    {
        switch (method)
        {
            case "List": { var r = await _sut.ListAsync(new PaginationParams()); return (r.StatusCode, r.ErrorCode); }
            case "Get": { var r = await _sut.GetAsync(Editor.Id); return (r.StatusCode, r.ErrorCode); }
            case "Create": { var r = await _sut.CreateAsync(new CreateRoleDto("Viewer", null, RoleType.Application)); return (r.StatusCode, r.ErrorCode); }
            case "Update": { var r = await _sut.UpdateAsync(Editor.Id, new UpdateRoleDto("Writer", null)); return (r.StatusCode, r.ErrorCode); }
            case "Delete": { var r = await _sut.DeleteAsync(Editor.Id); return (r.StatusCode, r.ErrorCode); }
            case "GetDefault": { var r = await _sut.GetDefaultAsync(); return (r.StatusCode, r.ErrorCode); }
            case "SetDefault": { var r = await _sut.SetDefaultAsync(Editor.Id); return (r.StatusCode, r.ErrorCode); }
            case "ClearDefault": { var r = await _sut.ClearDefaultAsync(); return (r.StatusCode, r.ErrorCode); }
            default: throw new ArgumentOutOfRangeException(nameof(method), method, null);
        }
    }

    private static async Task<OperationResult> RunAsync(Func<CancellationToken, Task> operation)
    {
        await operation(CancellationToken.None);
        return OperationResult.Ok();
    }

    private void GivenRole(RoleDto role)
        => _roles.GetByIdAsync(role.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleDto>.Ok(role));

    private void GivenDefault(Guid roleId)
        => _tenants.GetDefaultRoleIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(OperationResult<Guid?>.Ok(roleId));

    private void GivenUsage(RoleUsageDto usage)
        => _roles.GetUsageAsync(Editor.Id, Arg.Any<CancellationToken>()).Returns(OperationResult<RoleUsageDto>.Ok(usage));
}
